using System;

namespace PhinixClient.Framework
{
    /// <summary>Pixel metadata only. Zero/zero means unknown; measuring must not resolve an asset.</summary>
    public readonly struct ClientPresentationImageSize
    {
        public ClientPresentationImageSize(int width, int height)
        {
            if (width < 0 || height < 0 || (width == 0) != (height == 0)) throw new ArgumentException("PresentationImageSizeInvalid");
            Width = width; Height = height;
        }
        public int Width { get; }
        public int Height { get; }
    }

    /// <summary>Borrowed passive image. The producer retains resource ownership.
    /// Metadata/Revision are pure reads. Acquire runs only for visible drawing on the
    /// game main thread; it must not handle input or dispatch business commands.</summary>
    public interface IClientPresentationImage
    {
        ClientPresentationImageSize Size { get; }
        long Revision { get; }
        IClientPresentationImageLease Acquire();
    }

    /// <summary>A short drawing lease, never a transfer of image/Thing/Pawn ownership.
    /// Owner invalidation or revision changes revoke it. Dispose releases references,
    /// not the producer's asset. A renderer must not cache the lease between frames.</summary>
    public interface IClientPresentationImageLease : IDisposable
    {
        bool IsValid { get; }
    }
}
