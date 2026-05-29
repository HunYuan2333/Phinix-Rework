using Verse;
using Verse.Sound;

namespace PhinixClient.Framework
{
    internal sealed class ClientSoundService : IClientSoundService
    {
        private readonly Client client;

        public ClientSoundService(Client client)
        {
            this.client = client;
        }

        public void Enqueue(SoundDef soundDef)
        {
            client?.EnqueueSound(soundDef);
        }
    }
}
