package dev.mcskylines.bridge;

import java.io.EOFException;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.net.SocketTimeoutException;
import java.security.SecureRandom;
import java.util.Collection;
import java.util.Objects;
import java.util.Queue;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.Executors;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;
import java.util.function.Consumer;

/**
 * The guest side of SKBR v1 (protocol/bridge-v1.md). All socket I/O runs on daemon threads; the
 * application thread only calls {@link #send}, {@link #poll}/{@link #drainTo} and {@link #shutdown},
 * none of which block.
 */
public final class BridgeGuest {
	public static final int DEFAULT_PORT = 47615;
	public static final long DEFAULT_MAX_QUEUED_BYTES = 64L * 1024 * 1024;

	public record Config(int port, String appProtocol, int appMajor, int appMinor, String peerName,
			String peerVersion, long retryMs, int fastAttempts, long slowRetryMs, long welcomeTimeoutMs,
			long maxQueuedBytes, Consumer<String> logger) {
		public Config {
			Objects.requireNonNull(appProtocol);
			Objects.requireNonNull(peerName);
			Objects.requireNonNull(peerVersion);
			Objects.requireNonNull(logger);
		}

		public static Config of(String appProtocol, int appMajor, int appMinor, String peerName, String peerVersion) {
			return new Config(DEFAULT_PORT, appProtocol, appMajor, appMinor, peerName, peerVersion, 1000, 10, 5000,
				5000, DEFAULT_MAX_QUEUED_BYTES, msg -> {
				});
		}

		public Config withPort(int port) {
			return new Config(port, appProtocol, appMajor, appMinor, peerName, peerVersion, retryMs, fastAttempts,
				slowRetryMs, welcomeTimeoutMs, maxQueuedBytes, logger);
		}

		public Config withRetry(long retryMs, int fastAttempts, long slowRetryMs) {
			return new Config(port, appProtocol, appMajor, appMinor, peerName, peerVersion, retryMs, fastAttempts,
				slowRetryMs, welcomeTimeoutMs, maxQueuedBytes, logger);
		}

		public Config withMaxQueuedBytes(long maxQueuedBytes) {
			return new Config(port, appProtocol, appMajor, appMinor, peerName, peerVersion, retryMs, fastAttempts,
				slowRetryMs, welcomeTimeoutMs, maxQueuedBytes, logger);
		}

		public Config withLogger(Consumer<String> logger) {
			return new Config(port, appProtocol, appMajor, appMinor, peerName, peerVersion, retryMs, fastAttempts,
				slowRetryMs, welcomeTimeoutMs, maxQueuedBytes, logger);
		}
	}

	private enum Outcome { RETRY, SESSION_ENDED, STOP }

	private final Config config;
	private final Queue<BridgeEvent> events = new ConcurrentLinkedQueue<>();
	private final SecureRandom random = new SecureRandom();
	private final long startNanos = System.nanoTime();
	private final CountDownLatch stopped = new CountDownLatch(1);
	private final Object retryWait = new Object();
	private final ScheduledExecutorService closer = Executors.newSingleThreadScheduledExecutor(r -> daemon(r, "closer"));
	private final AtomicBoolean started = new AtomicBoolean();

	private volatile boolean stopping;
	private volatile int stopCode = Goodbye.SHUTTING_DOWN;
	private volatile String stopReason = "";
	private volatile BridgeState state = BridgeState.DISCONNECTED;
	private volatile Socket handshakeSocket;
	private volatile Session session;
	private volatile Welcome peer;

	public BridgeGuest(Config config) {
		this.config = Objects.requireNonNull(config);
	}

	public void start() {
		if (started.compareAndSet(false, true)) {
			daemon(this::run, "manager").start();
		}
	}

	public BridgeState state() {
		return state;
	}

	/** The accepted WELCOME of the current session, or null when not connected. */
	public Welcome peer() {
		return peer;
	}

	/**
	 * Queues an application frame. Returns false when there is no connected session or the frame overflowed the
	 * outbound queue (which ends the session with GOODBYE(BACKPRESSURE)).
	 */
	public boolean send(int type, byte[] payload) {
		if (type < FrameCodec.APP_MIN || type > 0xFFFF) {
			throw new IllegalArgumentException("application frame types are 0x0100-0xFFFF, got 0x" + Integer.toHexString(type));
		}
		if (payload.length > FrameCodec.MAX_PAYLOAD) {
			throw new IllegalArgumentException("payload exceeds 16 MiB");
		}
		Session s = session;
		return s != null && s.enqueue(FrameCodec.encode(type, payload), payload.length);
	}

	/**
	 * Queues an application frame that supersedes any still-unsent frame of the same type ("latest value
	 * wins"), for per-frame state such as PLAYER_STATE: at most one frame per type is ever waiting, so a
	 * slow reader cannot make the queue grow. Returns false when there is no connected session.
	 */
	public boolean sendLatest(int type, byte[] payload) {
		if (type < FrameCodec.APP_MIN || type > 0xFFFF) {
			throw new IllegalArgumentException("application frame types are 0x0100-0xFFFF, got 0x" + Integer.toHexString(type));
		}
		if (payload.length > FrameCodec.MAX_PAYLOAD) {
			throw new IllegalArgumentException("payload exceeds 16 MiB");
		}
		Session s = session;
		return s != null && s.enqueueLatest(type, FrameCodec.encode(type, payload));
	}

	/** Payload bytes queued by {@link #send} and not yet written to the socket; 0 without a session. */
	public long queuedBytes() {
		Session s = session;
		return s == null ? 0 : s.queuedBytes.get();
	}

	/** Hands every pending event to {@code sink} on the calling thread; returns how many. */
	public int poll(Consumer<? super BridgeEvent> sink) {
		int n = 0;
		for (BridgeEvent e; (e = events.poll()) != null; n++) {
			sink.accept(e);
		}
		return n;
	}

	public int drainTo(Collection<? super BridgeEvent> sink) {
		return poll(sink::add);
	}

	/** Stops retrying and ends any session with GOODBYE(code, reason). Does not block. */
	public void shutdown(int code, String reason) {
		stopCode = code;
		stopReason = reason;
		stopping = true;
		synchronized (retryWait) {
			retryWait.notifyAll();
		}
		Session s = session;
		if (s != null) {
			s.end(DisconnectCause.LOCAL_GOODBYE, code, reason, true);
		}
		closeQuietly(handshakeSocket);
		if (!started.get()) {
			stopped.countDown();
		}
	}

	/** Waits until the bridge threads have finished after {@link #shutdown} or a final rejection. */
	public boolean awaitStopped(long timeoutMs) throws InterruptedException {
		return stopped.await(timeoutMs, TimeUnit.MILLISECONDS);
	}

	private void run() {
		try {
			int attempts = 0;
			while (!stopping) {
				if (state != BridgeState.CONNECTING) {
					setState(BridgeState.CONNECTING, "127.0.0.1:" + config.port());
				}
				Outcome outcome = attempt();
				if (outcome == Outcome.STOP) {
					return;
				}
				if (outcome == Outcome.SESSION_ENDED) {
					attempts = 0;
				}
				long delay = attempts < config.fastAttempts() ? config.retryMs() : config.slowRetryMs();
				attempts++;
				synchronized (retryWait) {
					if (!stopping) {
						retryWait.wait(Math.max(1, delay));
					}
				}
			}
		} catch (InterruptedException e) {
			Thread.currentThread().interrupt();
		} catch (RuntimeException e) {
			log("bridge thread failed: " + e);
		} finally {
			if (state != BridgeState.REJECTED) {
				setState(BridgeState.DISCONNECTED, "stopped");
			}
			closer.shutdown();
			stopped.countDown();
		}
	}

	private Outcome attempt() {
		Socket sock = new Socket();
		handshakeSocket = sock;
		try {
			try {
				sock.setTcpNoDelay(true);
				sock.connect(new InetSocketAddress(InetAddress.getLoopbackAddress(), config.port()), 1000);
			} catch (IOException e) {
				return stopping ? Outcome.STOP : Outcome.RETRY;
			}
			if (stopping) {
				return Outcome.STOP;
			}
			setState(BridgeState.HANDSHAKING, "");
			Welcome w;
			try {
				OutputStream out = sock.getOutputStream();
				out.write(FrameCodec.encode(Hello.TYPE, new Hello(Hello.MAGIC, Hello.BRIDGE_VERSION,
					config.appProtocol(), config.appMajor(), config.appMinor(), config.peerName(),
					config.peerVersion(), random.nextLong()).encode()));
				sock.setSoTimeout((int) config.welcomeTimeoutMs());
				Frame f = FrameCodec.read(sock.getInputStream());
				if (f == null) {
					throw new EOFException("host closed before WELCOME");
				}
				Object reply = Messages.decodeBody(f);
				if (reply instanceof Goodbye g) {
					return handshakeFailed(DisconnectCause.PEER_GOODBYE, g.code(), g.reason());
				}
				if (!(reply instanceof Welcome welcome)) {
					throw new ProtocolException("expected WELCOME or GOODBYE before the handshake completes");
				}
				w = welcome;
			} catch (SocketTimeoutException e) {
				bestEffortGoodbye(sock, Goodbye.TIMEOUT, "no WELCOME within " + config.welcomeTimeoutMs() + " ms");
				return handshakeFailed(DisconnectCause.TIMEOUT, Goodbye.TIMEOUT, "no WELCOME");
			} catch (ProtocolException e) {
				bestEffortGoodbye(sock, Goodbye.PROTOCOL_ERROR, e.getMessage());
				return handshakeFailed(DisconnectCause.PROTOCOL_ERROR, Goodbye.PROTOCOL_ERROR, e.getMessage());
			} catch (IOException e) {
				return handshakeFailed(DisconnectCause.CONNECTION_LOST, -1, "closed before WELCOME");
			}
			if (!w.accepted()) {
				setState(BridgeState.REJECTED, w.rejectReason());
				emit(new BridgeEvent.Disconnected(DisconnectCause.REJECTED, w.rejectCode(), w.rejectReason()));
				if (w.rejectionIsFinal()) {
					log("host rejected this guest permanently (code " + w.rejectCode() + "): " + w.rejectReason());
					return Outcome.STOP;
				}
				return stopping ? Outcome.STOP : Outcome.RETRY;
			}
			return runSession(sock, w);
		} finally {
			handshakeSocket = null;
			closeQuietly(sock);
		}
	}

	private Outcome handshakeFailed(DisconnectCause cause, int code, String reason) {
		if (stopping) {
			emit(new BridgeEvent.Disconnected(DisconnectCause.LOCAL_GOODBYE, stopCode, stopReason));
			return Outcome.STOP;
		}
		emit(new BridgeEvent.Disconnected(cause, code, reason));
		setState(BridgeState.DISCONNECTED, reason);
		return Outcome.RETRY;
	}

	private Outcome runSession(Socket sock, Welcome w) {
		Session s = new Session(sock, w);
		peer = w;
		session = s;
		setState(BridgeState.CONNECTED, "session=" + Long.toUnsignedString(w.sessionId()) + " peer=" + w.peerName()
			+ " version=" + w.peerVersion());
		if (stopping) {
			s.end(DisconnectCause.LOCAL_GOODBYE, stopCode, stopReason, true);
		}
		s.run();
		session = null;
		peer = null;
		setState(BridgeState.DISCONNECTED, "");
		return stopping ? Outcome.STOP : Outcome.SESSION_ENDED;
	}

	private final class Session {
		private static final byte[] WAKE = new byte[0];
		/** Tells the writer to flush {@link #latest}; queued once per type when its slot goes from empty to full. */
		private static final byte[] LATEST = new byte[0];

		private final Socket sock;
		private final Welcome welcome;
		private final LinkedBlockingQueue<byte[]> outbound = new LinkedBlockingQueue<>();
		private final AtomicLong queuedBytes = new AtomicLong();
		private final java.util.concurrent.ConcurrentHashMap<Integer, byte[]> latest = new java.util.concurrent.ConcurrentHashMap<>();
		private final AtomicBoolean ended = new AtomicBoolean();
		private volatile byte[] finalGoodbye;

		Session(Socket sock, Welcome welcome) {
			this.sock = sock;
			this.welcome = welcome;
		}

		boolean enqueue(byte[] frame, int payloadBytes) {
			if (ended.get()) {
				return false;
			}
			if (queuedBytes.addAndGet(payloadBytes) > config.maxQueuedBytes()) {
				end(DisconnectCause.BACKPRESSURE, Goodbye.BACKPRESSURE, "outbound queue exceeded "
					+ config.maxQueuedBytes() + " bytes", true);
				return false;
			}
			outbound.add(frame);
			return true;
		}

		boolean enqueueLatest(int type, byte[] frame) {
			if (ended.get()) {
				return false;
			}
			if (latest.put(type, frame) == null) {
				outbound.add(LATEST);
			}
			return true;
		}

		/** First call wins; the writer thread sends the GOODBYE and closes, with a 1 s hard deadline. */
		void end(DisconnectCause cause, int code, String reason, boolean sendGoodbye) {
			if (!ended.compareAndSet(false, true)) {
				return;
			}
			if (sendGoodbye) {
				finalGoodbye = FrameCodec.encode(Goodbye.TYPE, new Goodbye(code, reason).encode());
			}
			setState(BridgeState.CLOSING, cause.wireName());
			emit(new BridgeEvent.Disconnected(cause, code, reason));
			outbound.add(WAKE);
			try {
				closer.schedule(() -> closeQuietly(sock), 1, TimeUnit.SECONDS);
			} catch (RuntimeException e) {
				closeQuietly(sock);
			}
		}

		/** Runs the reader on its own thread and the writer plus heartbeat on this one until the session ends. */
		void run() {
			daemon(this::read, "reader").start();
			long interval = Math.max(10, welcome.heartbeatIntervalMs());
			long seq = 0;
			long nextBeat = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(interval);
			try {
				OutputStream out = sock.getOutputStream();
				while (!ended.get()) {
					byte[] frame = outbound.poll(Math.max(0, nextBeat - System.nanoTime()), TimeUnit.NANOSECONDS);
					if (ended.get()) {
						break;
					}
					if (frame == null) {
						out.write(FrameCodec.encode(Heartbeat.TYPE, new Heartbeat(++seq & 0xFFFFFFFFL, uptimeMs()).encode()));
						nextBeat += TimeUnit.MILLISECONDS.toNanos(interval);
					} else if (frame == LATEST) {
						for (Integer type : latest.keySet()) {
							byte[] f = latest.remove(type);
							if (f != null) {
								out.write(f);
							}
						}
					} else if (frame != WAKE) {
						queuedBytes.addAndGet(-(frame.length - FrameCodec.HEADER_SIZE));
						out.write(frame);
					}
				}
				byte[] bye = finalGoodbye;
				if (bye != null) {
					out.write(bye);
					sock.shutdownOutput();
				}
			} catch (IOException e) {
				end(DisconnectCause.CONNECTION_LOST, -1, "write failed: " + e.getMessage(), false);
			} catch (InterruptedException e) {
				Thread.currentThread().interrupt();
				end(DisconnectCause.LOCAL_GOODBYE, Goodbye.SHUTTING_DOWN, "interrupted", true);
			} finally {
				closeQuietly(sock);
			}
		}

		private void read() {
			try {
				long timeout = welcome.peerTimeoutMs();
				sock.setSoTimeout((int) Math.min(Integer.MAX_VALUE, timeout));
				InputStream in = sock.getInputStream();
				while (!ended.get()) {
					Frame f = FrameCodec.read(in);
					if (f == null) {
						end(DisconnectCause.CONNECTION_LOST, -1, "host closed the connection", false);
						return;
					}
					Object body = Messages.decodeBody(f);
					if (body instanceof Goodbye g) {
						end(DisconnectCause.PEER_GOODBYE, g.code(), g.reason(), false);
						return;
					}
					if (body instanceof Frame app) {
						emit(new BridgeEvent.Message(app.type(), app.payload()));
					} else if (!(body instanceof Heartbeat)) {
						throw new ProtocolException("unexpected bridge frame 0x" + Integer.toHexString(f.type())
							+ " after the handshake");
					}
				}
			} catch (SocketTimeoutException e) {
				end(DisconnectCause.TIMEOUT, Goodbye.TIMEOUT, "no frame from the host for "
					+ welcome.peerTimeoutMs() + " ms", true);
			} catch (ProtocolException e) {
				end(DisconnectCause.PROTOCOL_ERROR, Goodbye.PROTOCOL_ERROR, e.getMessage(), true);
			} catch (IOException e) {
				end(DisconnectCause.CONNECTION_LOST, -1, String.valueOf(e.getMessage()), false);
			}
		}
	}

	private void bestEffortGoodbye(Socket sock, int code, String reason) {
		try {
			sock.getOutputStream().write(FrameCodec.encode(Goodbye.TYPE, new Goodbye(code, reason).encode()));
			sock.shutdownOutput();
		} catch (IOException ignored) {
			// best effort, per bridge-v1.md
		}
	}

	private long uptimeMs() {
		return TimeUnit.NANOSECONDS.toMillis(System.nanoTime() - startNanos);
	}

	private void setState(BridgeState next, String detail) {
		state = next;
		emit(new BridgeEvent.StateChanged(next, detail));
	}

	private void emit(BridgeEvent e) {
		events.add(e);
		switch (e) {
			case BridgeEvent.StateChanged s -> log("state " + s.state().wireName() + (s.detail().isEmpty() ? "" : " (" + s.detail() + ")"));
			case BridgeEvent.Disconnected d -> log("disconnected cause=" + d.cause().wireName() + " code=" + d.code() + " reason=" + d.reason());
			case BridgeEvent.Message m -> {
			}
		}
	}

	private void log(String msg) {
		try {
			config.logger().accept(msg);
		} catch (RuntimeException ignored) {
			// a broken logger must not take the link down
		}
	}

	private static Thread daemon(Runnable r, String name) {
		Thread t = new Thread(r, "skbr-guest-" + name);
		t.setDaemon(true);
		return t;
	}

	private static void closeQuietly(Socket s) {
		if (s != null) {
			try {
				s.close();
			} catch (IOException ignored) {
				// already closed
			}
		}
	}
}
