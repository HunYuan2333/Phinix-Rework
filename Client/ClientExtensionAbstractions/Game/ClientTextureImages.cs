using System;
using UnityEngine;

namespace PhinixClient.Framework
{
    // Shared game-facing bridge, available equally to every producer. Texture stays
    // here rather than in the ordinary row/image contracts; no GPU readback/copy.
    public interface IClientTextureImageLease : IClientPresentationImageLease
    {
        bool TryGetTexture(out Texture texture);
    }

    public sealed class ClientTextureImage : IClientPresentationImage
    {
        private Func<Texture> resolve;
        private Func<bool> isCurrent;
        private Func<long> revision;
        private bool invalidated;
        private ClientTextureImage(Func<Texture> resolve, Func<bool> isCurrent,
            Func<long> revision, ClientPresentationImageSize size)
        {
            this.resolve = resolve ?? throw new ArgumentNullException(nameof(resolve));
            this.isCurrent = isCurrent ?? (() => true);
            this.revision = revision ?? (() => 0);
            Size = size;
        }
        public static ClientTextureImage FromTexture(Texture texture)
        {
            // Do not query dimensions or native state during publication/measurement.
            return ReferenceEquals(texture, null) ? null : new ClientTextureImage(() => texture, null, null, default(ClientPresentationImageSize));
        }
        public static ClientTextureImage FromSource(Func<Texture> resolve, Func<bool> isCurrent,
            Func<long> revision, ClientPresentationImageSize size)
        { return new ClientTextureImage(resolve, isCurrent ?? throw new ArgumentNullException(nameof(isCurrent)), revision, size); }
        public ClientPresentationImageSize Size { get; }
        public long Revision
        {
            get { if (invalidated) return -1; long value = revision(); return invalidated ? -1 : value; }
        }
        public void Invalidate()
        { invalidated = true; resolve = null; isCurrent = null; revision = null; }
        private bool Current
        {
            get { var guard = isCurrent; return !invalidated && guard != null && guard() && !invalidated; }
        }
        public IClientPresentationImageLease Acquire()
        {
            if (!Current) return null;
            long expected = Revision;
            if (!Current) return null;
            Texture texture = resolve();
            if (!Current || Revision != expected || texture == null) return null;
            return new Lease(this, expected, texture);
        }
        private sealed class Lease : IClientTextureImageLease
        {
            private ClientTextureImage source;
            private Texture texture;
            private readonly long expected;
            internal Lease(ClientTextureImage source, long expected, Texture texture)
            { this.source = source; this.expected = expected; this.texture = texture; }
            public bool IsValid => source != null && source.Current && source.Revision == expected && texture != null;
            public bool TryGetTexture(out Texture value)
            { value = IsValid ? texture : null; return value != null; }
            public void Dispose() { source = null; texture = null; }
        }
    }
}
