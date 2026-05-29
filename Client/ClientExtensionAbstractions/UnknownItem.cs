using Verse;

namespace PhinixClient
{
    public class UnknownItem : Thing
    {
        public override string Label => generateLabel();

        public string OriginalLabel;

        private string generateLabel()
        {
            return OriginalLabel != null
                ? string.Format("{0} ({1})", def.label, OriginalLabel)
                : def.label;
        }
    }
}
