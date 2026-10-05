package dev.mcskylines.bridge.conformance;

import dev.mcskylines.bridge.BridgeEvent;
import dev.mcskylines.bridge.BridgeGuest;
import dev.mcskylines.bridge.Goodbye;
import java.io.IOException;
import java.io.PrintStream;
import java.nio.charset.StandardCharsets;

/**
 * Guest mode of the IUT contract in protocol/reference/conformance.py:
 * {@code guest --port P --app A --major M --retry-ms R}. Echoes 0x0100 as 0x0101, prints READY and EVENT lines,
 * and ends with GOODBYE(SHUTTING_DOWN) when stdin closes.
 */
public final class Main {
	private static final int ECHO_REQUEST = 0x0100;
	private static final int ECHO_REPLY = 0x0101;
	private static final PrintStream OUT = new PrintStream(System.out, true, StandardCharsets.UTF_8);

	private Main() {
	}

	public static void main(String[] args) throws InterruptedException {
		if (args.length == 0 || !args[0].equals("guest")) {
			System.err.println("usage: bridge guest --port P [--app NAME] [--major N] [--retry-ms MS]");
			System.exit(2);
		}
		int port = BridgeGuest.DEFAULT_PORT;
		String app = "skbr-conformance";
		int major = 1;
		long retryMs = 1000;
		for (int i = 1; i + 1 < args.length; i += 2) {
			switch (args[i]) {
				case "--port" -> port = Integer.parseInt(args[i + 1]);
				case "--app" -> app = args[i + 1];
				case "--major" -> major = Integer.parseInt(args[i + 1]);
				case "--retry-ms" -> retryMs = Long.parseLong(args[i + 1]);
				default -> {
					System.err.println("unknown option " + args[i]);
					System.exit(2);
				}
			}
		}
		BridgeGuest guest = new BridgeGuest(BridgeGuest.Config.of(app, major, 0, "mcskylines-bridge (conformance)", "0.1.0")
			.withPort(port).withRetry(retryMs, 10, 5000).withLogger(msg -> System.err.println("[bridge] " + msg)));

		Thread stdin = new Thread(() -> {
			try {
				System.in.transferTo(java.io.OutputStream.nullOutputStream());
			} catch (IOException ignored) {
				// treat a broken stdin as closed
			}
		}, "stdin-watch");
		stdin.setDaemon(true);
		stdin.start();

		guest.start();
		OUT.println("READY");
		while (stdin.isAlive()) {
			guest.poll(e -> handle(guest, e));
			Thread.sleep(5);
		}
		guest.shutdown(Goodbye.SHUTTING_DOWN, "stdin closed");
		guest.awaitStopped(3000);
		guest.poll(e -> handle(guest, e));
		OUT.flush();
		System.exit(0);
	}

	private static void handle(BridgeGuest guest, BridgeEvent e) {
		switch (e) {
			case BridgeEvent.StateChanged s -> OUT.println("EVENT state " + s.state().wireName()
				+ (s.detail().isEmpty() ? "" : " " + s.detail()));
			case BridgeEvent.Disconnected d -> OUT.println("EVENT disconnected cause=" + d.cause().wireName()
				+ " code=" + d.code() + " reason=" + d.reason());
			case BridgeEvent.Message m -> {
				if (m.type() == ECHO_REQUEST) {
					guest.send(ECHO_REPLY, m.payload());
				} else {
					OUT.printf("EVENT app type=0x%04x bytes=%d%n", m.type(), m.payload().length);
				}
			}
		}
	}
}
