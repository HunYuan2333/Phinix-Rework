using UnityEngine;

namespace Phinix.ChatExtension.Client
{
    public interface IChatTabContent
    {
        void Draw(Rect inRect);

        void ScrollToBottom();
    }
}
