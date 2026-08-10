using System;
using System.Collections.Generic;
using System.Text;
using Verse;

namespace Phinix.LegacyTalentTradeExtension.Client
{
    /// <summary>
    /// Serializes/deserializes TradeOffer to/from Base64 for protocol transmission.
    /// Format: simple pipe-delimited fields (not JSON, to avoid dependency).
    /// </summary>
    public static class TradeOfferSerializer
    {
        // Format: silver|pawnCount|b64Summary1|...|pawnDataCount|b64PawnData1|...|manifestCount|b64Manifest1|...
        public static string ToBase64(TradeOffer offer)
        {
            if (offer == null) return "";

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(offer.SilverAmount);
                sb.Append('|');
                sb.Append(offer.Pawns.Count);
                for (int i = 0; i < offer.Pawns.Count; i++)
                {
                    sb.Append('|');
                    sb.Append(offer.Pawns[i].ToBase64());
                }
                sb.Append('|');
                sb.Append(offer.PawnData.Count);
                for (int i = 0; i < offer.PawnData.Count; i++)
                {
                    sb.Append('|');
                    sb.Append(offer.PawnData[i]);
                }

                // Optional tail keeps old clients compatible: they stop after
                // PawnData and ignore the additional manifest fields.
                sb.Append('|');
                int manifestCount = Math.Min(offer.PawnData.Count, offer.PawnManifestData.Count);
                sb.Append(manifestCount);
                for (int i = 0; i < manifestCount; i++)
                {
                    sb.Append('|');
                    sb.Append(offer.PawnManifestData[i] ?? string.Empty);
                }

                return Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
            }
            catch (Exception ex)
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】TradeOfferSerializer.ToBase64 failed: " + ex);
                return "";
            }
        }

        public static TradeOffer FromBase64(string b64)
        {
            if (string.IsNullOrEmpty(b64)) return null;

            try
            {
                string raw = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                string[] parts = raw.Split('|');
                int idx = 0;

                TradeOffer offer = new TradeOffer();

                // Silver
                int silver;
                if (int.TryParse(parts[idx++], out silver))
                    offer.SilverAmount = silver;

                // Pawn summaries
                int pawnCount;
                if (int.TryParse(parts[idx++], out pawnCount))
                {
                    for (int i = 0; i < pawnCount && idx < parts.Length; i++)
                    {
                        PawnSummary summary = PawnSummary.FromBase64(parts[idx++]);
                        if (summary != null)
                            offer.Pawns.Add(summary);
                    }
                }

                // Pawn data
                if (idx < parts.Length)
                {
                    int dataCount;
                    if (int.TryParse(parts[idx++], out dataCount))
                    {
                        for (int i = 0; i < dataCount && idx < parts.Length; i++)
                        {
                            offer.PawnData.Add(parts[idx++]);
                        }

                        // Optional metadata tail. Legacy offers simply end
                        // here and remain valid, but will be shown as unknown
                        // compatibility by the receiver.
                        if (idx < parts.Length)
                        {
                            int manifestCount;
                            if (int.TryParse(parts[idx++], out manifestCount))
                            {
                                for (int i = 0; i < manifestCount && idx < parts.Length; i++)
                                    offer.PawnManifestData.Add(parts[idx++]);
                            }
                        }
                    }
                }

                return offer;
            }
            catch (Exception ex)
            {
                LegacyTalentTradeRuntime.LogError("【三角洲贸易】TradeOfferSerializer.FromBase64 failed: " + ex);
                return null;
            }
        }
    }
}
