using System;
using MinecraftSkylines.Mod.Blocks;
using MinecraftSkylines.Protocol;
using Skylines.Host;
using Skylines.Host.Rendering;
using UnityEngine;

namespace MinecraftSkylines.Mod.Entities
{
    /// <summary>
    /// Minor 14: Minecraft's entities (ENTITY_MODEL, ENTITY_TEXTURE, ENTITY_STATES) drawn in the city scene by the
    /// Minecraft-free <see cref="BoxModelRenderer"/>, in city and Minecraft mode, from every camera. Main thread only.
    /// </summary>
    internal sealed class EntityLink : IDisposable
    {
        private const float DrawDistance = 256f;
        private readonly HostLog _log;
        private readonly BoxModelRenderer _renderer;
        private long _states;

        public EntityLink(HostLog log, BlockRenderer blocks)
        {
            _log = log;
            int layer = LayerMask.NameToLayer("Props");
            _renderer = new BoxModelRenderer(layer >= 0 ? layer : 10, blocks.CreateEntityMaterial);
        }

        public static bool Handles(ushort type)
        {
            return type >= AppProtocol.EntityModelType && type <= AppProtocol.EntityStatesType;
        }

        public void Handle(ushort type, byte[] payload)
        {
            switch (type)
            {
                case AppProtocol.EntityModelType:
                    {
                        EntityModel m = EntityModel.Decode(payload);
                        var parents = new int[m.Parts.Length];
                        var quads = new float[m.Parts.Length][];
                        for (int i = 0; i < m.Parts.Length; i++)
                        {
                            parents[i] = m.Parts[i].Parent == EntityModel.NoParent ? -1 : m.Parts[i].Parent;
                            quads[i] = m.Parts[i].Quads;
                        }
                        _renderer.SetModel(m.ModelId, parents, quads);
                        _log.Info("entities: model " + m.ModelId + " '" + m.Name + "', " + m.Parts.Length + " parts");
                        break;
                    }
                case AppProtocol.EntityTextureType:
                    {
                        EntityTexture t = EntityTexture.Decode(payload);
                        if (t.Format != EntityTexture.FormatPng || !_renderer.SetTexture(t.TextureId, t.Data))
                            _log.Warn("entities: texture " + t.TextureId + " (format " + t.Format + ", " + t.Data.Length + " bytes) did not decode");
                        break;
                    }
                default:
                    OnStates(EntityStates.Decode(payload));
                    break;
            }
        }

        private void OnStates(EntityStates s)
        {
            _states++;
            var list = new BoxEntity[s.Entities.Length];
            for (int i = 0; i < list.Length; i++)
            {
                EntityState e = s.Entities[i];
                var draws = new BoxDraw[e.Draws.Length];
                for (int d = 0; d < draws.Length; d++)
                {
                    EntityDraw w = e.Draws[d];
                    var poses = new float[w.Parts.Length * 9];
                    var flags = new byte[w.Parts.Length];
                    for (int p = 0; p < w.Parts.Length; p++)
                    {
                        EntityPartPose q = w.Parts[p];
                        int o = p * 9;
                        poses[o] = q.Px; poses[o + 1] = q.Py; poses[o + 2] = q.Pz;
                        poses[o + 3] = q.XRot; poses[o + 4] = q.YRot; poses[o + 5] = q.ZRot;
                        poses[o + 6] = q.XScale; poses[o + 7] = q.YScale; poses[o + 8] = q.ZScale;
                        flags[p] = q.Flags;
                    }
                    uint c = w.Color;
                    draws[d] = new BoxDraw
                    {
                        Model = w.ModelId, Texture = w.TextureId, Matrix = w.Matrix, Poses = poses, Flags = flags,
                        Tint = new Color32((byte)c, (byte)(c >> 8), (byte)(c >> 16), (byte)(c >> 24)),
                    };
                }
                list[i] = new BoxEntity { Id = e.EntityId, X = e.X, Y = e.Y, Z = e.Z, Draws = draws };
            }
            _renderer.SetFrame(list, Time.realtimeSinceStartup);
        }

        /// <summary>Every frame from the pump's LateUpdate; draws only while a city is loaded.</summary>
        public void LateUpdate(bool cityReady)
        {
            Camera cam = Camera.main;
            if (cam == null) return;
            _renderer.LateUpdate(cityReady, Time.realtimeSinceStartup, cam.transform.position, DrawDistance);
        }

        /// <summary>City unloading: the entities belong to it; models and textures stay for the next city.</summary>
        public void OnLevelUnloading()
        {
            _renderer.ClearEntities();
        }

        public void OnDisconnect()
        {
            _renderer.Clear();
        }

        public string OverlayText()
        {
            if (_states == 0) return "";
            return "Entities: " + _renderer.EntityCount + " (" + _renderer.DrawnLastFrame + " parts drawn), " + _renderer.ModelCount + " models, "
                + _renderer.TextureCount + " textures";
        }

        public void Dispose()
        {
            _renderer.Dispose();
        }
    }
}
