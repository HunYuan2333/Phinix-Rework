using UnityEngine;

namespace PhinixClient.Framework
{
    public interface INoticeBannerProvider
    {
        float CurrentHeight { get; }

        void Draw(Rect inRect);
    }
}
