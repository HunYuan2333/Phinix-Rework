using UnityEngine;

namespace PhinixClient
{
    public interface IMainTabProvider
    {
        string TabLabel { get; }
        float TabOrder { get; }
        void Draw(Rect inRect);
    }
}
