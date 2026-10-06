package dev.mcskylines.shadow;

/** Receives one planned shadow cell. */
@FunctionalInterface
public interface CellSink {
	void accept(int x, int y, int z, String block);
}
