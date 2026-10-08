package dev.mcskylines.world;

import static org.junit.jupiter.api.Assertions.*;

import dev.mcskylines.protocol.CityEntities;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.util.ArrayList;
import java.util.List;
import java.util.Random;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.NbtIo;
import org.junit.jupiter.api.Test;

class EntityBlobTest {
    private static CompoundTag entity(int i, int padding) {
        CompoundTag t = new CompoundTag();
        t.putString("id", "minecraft:cow");
        t.putInt("n", i);
        byte[] b = new byte[padding];
        new Random(i).nextBytes(b); // incompressible, so size limits bite
        t.putByteArray("pad", b);
        return t;
    }

    private static List<CompoundTag> list(int n, int padding) {
        List<CompoundTag> l = new ArrayList<>();
        for (int i = 0; i < n; i++) l.add(entity(i, padding));
        return l;
    }

    private static byte[] gz(CompoundTag t) throws IOException {
        ByteArrayOutputStream bo = new ByteArrayOutputStream();
        NbtIo.writeCompressed(t, bo);
        return bo.toByteArray();
    }

    @Test
    void constants() {
        assertEquals(1, EntityBlob.VERSION);
        assertEquals(1024, EntityBlob.MAX_ENTITIES);
    }

    @Test
    void roundTripPreservesCompoundsOrderAndDataVersion() throws Exception {
        List<CompoundTag> in = list(5, 8);
        EntityBlob.Decoded d = EntityBlob.decode(EntityBlob.encode(in, 4325));
        assertEquals(4325, d.dataVersion());
        assertEquals(in, d.entities());
    }

    @Test
    void emptyListEncodesValidRoot() throws Exception {
        byte[] data = EntityBlob.encode(List.of(), 99);
        assertTrue(data.length > 0);
        EntityBlob.Decoded d = EntityBlob.decode(data);
        assertEquals(99, d.dataVersion());
        assertTrue(d.entities().isEmpty());
    }

    @Test
    void emptyArrayDecodesToEmpty() throws Exception {
        EntityBlob.Decoded d = EntityBlob.decode(new byte[0]);
        assertEquals(0, d.dataVersion());
        assertTrue(d.entities().isEmpty());
    }

    @Test
    void keepsOnlyFirstMaxEntities() throws Exception {
        List<CompoundTag> in = list(EntityBlob.MAX_ENTITIES + 10, 0);
        EntityBlob.Decoded d = EntityBlob.decode(EntityBlob.encode(in, 1));
        assertEquals(EntityBlob.MAX_ENTITIES, d.entities().size());
        assertEquals(in.subList(0, EntityBlob.MAX_ENTITIES), d.entities());
    }

    @Test
    void oversizeDropsFromTheEndUntilItFits() throws Exception {
        List<CompoundTag> in = list(40, 2000);
        int max = 20000;
        byte[] data = EntityBlob.encode(in, 1, max);
        assertTrue(data.length <= max, "length " + data.length);
        EntityBlob.Decoded d = EntityBlob.decode(data);
        assertFalse(d.entities().isEmpty());
        assertTrue(d.entities().size() < in.size());
        assertEquals(in.subList(0, d.entities().size()), d.entities());
    }

    @Test
    void tinyLimitNeverThrows() {
        assertNotNull(EntityBlob.encode(list(3, 100), 1, 1));
    }

    @Test
    void defaultLimitIsCityEntitiesMax() {
        assertTrue(EntityBlob.encode(list(3, 10), 1).length <= CityEntities.MAX_LENGTH);
    }

    @Test
    void decodeRejectsGarbage() {
        assertThrows(IOException.class, () -> EntityBlob.decode(new byte[] {1, 2, 3, 4, 5}));
    }

    @Test
    void decodeRejectsMissingVersion() {
        CompoundTag t = new CompoundTag();
        t.putInt("DataVersion", 1);
        t.put("entities", new ListTag());
        assertThrows(IOException.class, () -> EntityBlob.decode(gz(t)));
    }

    @Test
    void decodeRejectsWrongVersion() {
        CompoundTag t = new CompoundTag();
        t.putInt("version", 2);
        t.putInt("DataVersion", 1);
        t.put("entities", new ListTag());
        assertThrows(IOException.class, () -> EntityBlob.decode(gz(t)));
    }

    @Test
    void decodeRejectsMissingEntities() {
        CompoundTag t = new CompoundTag();
        t.putInt("version", 1);
        t.putInt("DataVersion", 1);
        assertThrows(IOException.class, () -> EntityBlob.decode(gz(t)));
    }

    @Test
    void priorityOrdersPersistentMobsFirst() {
        assertEquals(0, EntityBlob.priority(true, true));
        assertEquals(1, EntityBlob.priority(true, false));
        assertEquals(2, EntityBlob.priority(false, false));
        assertEquals(2, EntityBlob.priority(false, true));
    }
}
