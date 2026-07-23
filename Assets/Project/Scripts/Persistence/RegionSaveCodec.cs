using System;
using System.IO;
using System.Text;
using Project.Scripts.DataTypes;
using Project.Scripts.DataTypes.SaveData;

namespace Project.Scripts.Persistence
{
    public static class RegionSaveCodec
    {
        private const uint Magic = 0x47525357; // "WSRG" in little-endian bytes.
        private const int MaxRecordsPerCollection = 1_000_000;
        private const int MaxComponentBytes = 16 * 1024 * 1024;

        public static void Write(Stream stream, RegionSaveData save)
        {
            using BinaryWriter writer = new(
                stream,
                Encoding.UTF8,
                leaveOpen: true);

            writer.Write(Magic);
            writer.Write(RegionSaveData.CurrentVersion);
            writer.Write(save.coordinate.x);
            writer.Write(save.coordinate.y);
            writer.Write(save.lastSimulatedTick);
            writer.Write(save.nextScheduledTick);

            WriteRegionComponents(writer, save);
            WriteChunks(writer, save);
        }

        public static RegionSaveData Read(Stream stream)
        {
            using BinaryReader reader = new(
                stream,
                Encoding.UTF8,
                leaveOpen: true);

            if (reader.ReadUInt32() != Magic)
                throw new InvalidDataException("Not a WorldSaver region file.");

            ushort version = reader.ReadUInt16();

            if (version == 0 || version > RegionSaveData.CurrentVersion)
            {
                throw new InvalidDataException(
                    $"Unsupported region save version {version}.");
            }

            RegionSaveData save = new()
            {
                version = version,
                coordinate = new UnityEngine.Vector2Int(
                    reader.ReadInt32(),
                    reader.ReadInt32()),
                lastSimulatedTick = reader.ReadInt64(),
                nextScheduledTick = reader.ReadInt64()
            };

            ReadRegionComponents(reader, save);
            ReadChunks(reader, save);

            return save;
        }

        private static void WriteRegionComponents(
            BinaryWriter writer,
            RegionSaveData save)
        {
            int count = save.components?.Count ?? 0;
            writer.Write(count);

            for (int i = 0; i < count; i++)
                WriteRegionComponent(writer, save.components[i]);
        }

        private static void ReadRegionComponents(
            BinaryReader reader,
            RegionSaveData save)
        {
            int count = ReadCount(reader, "region component");

            for (int i = 0; i < count; i++)
                save.components.Add(ReadRegionComponent(reader));
        }

        private static void WriteChunks(
            BinaryWriter writer,
            RegionSaveData save)
        {
            int count = save.changedChunks?.Count ?? 0;
            writer.Write(count);

            for (int i = 0; i < count; i++)
            {
                ChunkState chunk = save.changedChunks[i];
                writer.Write(chunk.localChunkIndex);
                writer.Write(chunk.lastSimulatedTick);

                int entityCount = chunk.entities?.Count ?? 0;
                writer.Write(entityCount);

                for (int entityIndex = 0; entityIndex < entityCount; entityIndex++)
                    WriteEntity(writer, chunk.entities[entityIndex]);

                int componentCount = chunk.components?.Count ?? 0;
                writer.Write(componentCount);

                for (int componentIndex = 0;
                     componentIndex < componentCount;
                     componentIndex++)
                {
                    WritePersistentComponent(
                        writer,
                        chunk.components[componentIndex]);
                }

                int tileCount = chunk.tileOverrides?.Count ?? 0;
                writer.Write(tileCount);
                for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
                    WriteTileOverride(writer, chunk.tileOverrides[tileIndex]);
            }
        }

        private static void ReadChunks(
            BinaryReader reader,
            RegionSaveData save)
        {
            int chunkCount = ReadCount(reader, "chunk");

            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                ChunkState chunk = new()
                {
                    localChunkIndex = reader.ReadUInt16(),
                    lastSimulatedTick = reader.ReadInt64()
                };

                int entityCount = ReadCount(reader, "entity");

                for (int entityIndex = 0; entityIndex < entityCount; entityIndex++)
                    chunk.entities.Add(ReadEntity(reader));

                int componentCount = ReadCount(reader, "chunk component");

                for (int componentIndex = 0;
                     componentIndex < componentCount;
                     componentIndex++)
                {
                    chunk.components.Add(ReadPersistentComponent(reader));
                }

                if (save.version >= 2)
                {
                    int tileCount = ReadCount(reader, "tile override");
                    for (int tileIndex = 0; tileIndex < tileCount; tileIndex++)
                        chunk.tileOverrides.Add(ReadTileOverride(reader));
                }

                save.changedChunks.Add(chunk);
            }
        }

        private static void WriteTileOverride(BinaryWriter writer, TileOverrideData tile)
        {
            writer.Write(tile.localX);
            writer.Write(tile.localY);
            writer.Write((byte)tile.layer);
            writer.Write((byte)tile.kind);
            writer.Write(tile.tileId);
        }

        private static TileOverrideData ReadTileOverride(BinaryReader reader)
        {
            TileOverrideData tile = new()
            {
                localX = reader.ReadByte(),
                localY = reader.ReadByte(),
                layer = (PersistentTileLayer)reader.ReadByte(),
                kind = (TileOverrideKind)reader.ReadByte(),
                tileId = reader.ReadInt32()
            };

            if (tile.localX >= ChunkBuildResult.ChunkSize ||
                tile.localY >= ChunkBuildResult.ChunkSize)
                throw new InvalidDataException("Tile override is outside its chunk.");
            if (!Enum.IsDefined(typeof(PersistentTileLayer), tile.layer) ||
                !Enum.IsDefined(typeof(TileOverrideKind), tile.kind))
                throw new InvalidDataException("Invalid tile override enum value.");
            if ((tile.kind == TileOverrideKind.Place && tile.tileId < 0) ||
                (tile.kind == TileOverrideKind.Clear && tile.tileId != -1))
                throw new InvalidDataException("Invalid tile override tile ID.");

            return tile;
        }

        private static void WriteEntity(
            BinaryWriter writer,
            PersistentEntityRecord entity)
        {
            writer.Write(entity.id.value);
            writer.Write(entity.archetypeId);
            writer.Write((byte)entity.persistenceKind);
            writer.Write((byte)entity.existenceState);
            writer.Write(entity.lastSimulatedTick);

            int count = entity.components?.Count ?? 0;
            writer.Write(count);

            for (int i = 0; i < count; i++)
                WritePersistentComponent(writer, entity.components[i]);
        }

        private static PersistentEntityRecord ReadEntity(BinaryReader reader)
        {
            PersistentEntityRecord entity = new()
            {
                id = new NodeId(reader.ReadUInt64()),
                archetypeId = reader.ReadInt32(),
                persistenceKind = (EntityPersistenceKind)reader.ReadByte(),
                existenceState = (EntityExistenceState)reader.ReadByte(),
                lastSimulatedTick = reader.ReadInt64()
            };

            if (!Enum.IsDefined(typeof(EntityPersistenceKind), entity.persistenceKind) ||
                !Enum.IsDefined(typeof(EntityExistenceState), entity.existenceState))
            {
                throw new InvalidDataException("Invalid entity enum value.");
            }

            int count = ReadCount(reader, "entity component");

            for (int i = 0; i < count; i++)
                entity.components.Add(ReadPersistentComponent(reader));

            return entity;
        }

        private static void WritePersistentComponent(
            BinaryWriter writer,
            PersistenceComponentRecord component)
        {
            writer.Write(component.typeId);
            writer.Write(component.version);
            writer.Write(component.isAtBaseline);
            WriteBytes(writer, component.data);
        }

        private static PersistenceComponentRecord ReadPersistentComponent(
            BinaryReader reader)
        {
            return new PersistenceComponentRecord
            {
                typeId = reader.ReadUInt16(),
                version = reader.ReadUInt16(),
                isAtBaseline = reader.ReadBoolean(),
                data = ReadBytes(reader)
            };
        }

        private static void WriteRegionComponent(
            BinaryWriter writer,
            RegionComponentRecord component)
        {
            writer.Write(component.typeId);
            writer.Write(component.version);
            writer.Write(component.isAtBaseline);
            WriteBytes(writer, component.data);
        }

        private static RegionComponentRecord ReadRegionComponent(
            BinaryReader reader)
        {
            return new RegionComponentRecord
            {
                typeId = reader.ReadUInt16(),
                version = reader.ReadUInt16(),
                isAtBaseline = reader.ReadBoolean(),
                data = ReadBytes(reader)
            };
        }

        private static void WriteBytes(BinaryWriter writer, byte[] data)
        {
            data ??= Array.Empty<byte>();
            writer.Write(data.Length);
            writer.Write(data);
        }

        private static byte[] ReadBytes(BinaryReader reader)
        {
            int length = reader.ReadInt32();

            if (length < 0 || length > MaxComponentBytes)
                throw new InvalidDataException($"Invalid component size {length}.");

            byte[] data = reader.ReadBytes(length);

            if (data.Length != length)
                throw new EndOfStreamException("Truncated component payload.");

            return data;
        }

        private static int ReadCount(BinaryReader reader, string label)
        {
            int count = reader.ReadInt32();

            if (count < 0 || count > MaxRecordsPerCollection)
                throw new InvalidDataException($"Invalid {label} count {count}.");

            return count;
        }
    }
}
