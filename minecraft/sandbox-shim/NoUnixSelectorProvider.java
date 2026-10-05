package dev.mcskylines.sandboxshim;

import java.io.IOException;
import java.net.ProtocolFamily;
import java.net.StandardProtocolFamily;
import java.nio.channels.Channel;
import java.nio.channels.DatagramChannel;
import java.nio.channels.Pipe;
import java.nio.channels.ServerSocketChannel;
import java.nio.channels.SocketChannel;
import java.nio.channels.spi.AbstractSelector;
import java.nio.channels.spi.SelectorProvider;

/**
 * Build-host shim, never shipped. The agent sandbox's seccomp filter makes creating an AF_UNIX socket
 * fail with EPERM, and Fabric Loom 1.18's CurrentPlatform probe treats only UnsupportedOperationException
 * as "no Unix sockets", so it aborts plugin application. This provider reports UNIX as unsupported and
 * delegates everything else to the JDK default. See minecraft/sandbox-gradle.sh.
 */
public final class NoUnixSelectorProvider extends SelectorProvider {
	private static final SelectorProvider D = sun.nio.ch.DefaultSelectorProvider.get();

	@Override
	public DatagramChannel openDatagramChannel() throws IOException {
		return D.openDatagramChannel();
	}

	@Override
	public DatagramChannel openDatagramChannel(ProtocolFamily family) throws IOException {
		return D.openDatagramChannel(family);
	}

	@Override
	public Pipe openPipe() throws IOException {
		return D.openPipe();
	}

	@Override
	public AbstractSelector openSelector() throws IOException {
		return D.openSelector();
	}

	@Override
	public ServerSocketChannel openServerSocketChannel() throws IOException {
		return D.openServerSocketChannel();
	}

	@Override
	public ServerSocketChannel openServerSocketChannel(ProtocolFamily family) throws IOException {
		if (family == StandardProtocolFamily.UNIX) {
			throw new UnsupportedOperationException("Unix domain sockets are blocked in this sandbox");
		}
		return D.openServerSocketChannel(family);
	}

	@Override
	public SocketChannel openSocketChannel() throws IOException {
		return D.openSocketChannel();
	}

	@Override
	public SocketChannel openSocketChannel(ProtocolFamily family) throws IOException {
		if (family == StandardProtocolFamily.UNIX) {
			throw new UnsupportedOperationException("Unix domain sockets are blocked in this sandbox");
		}
		return D.openSocketChannel(family);
	}

	@Override
	public Channel inheritedChannel() throws IOException {
		return D.inheritedChannel();
	}
}
