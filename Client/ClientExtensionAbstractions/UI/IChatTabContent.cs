using UnityEngine;

namespace PhinixClient
{
    public interface IChatTabContent
    {
        void Draw(Rect inRect);

        void ScrollToBottom();
    }
}
