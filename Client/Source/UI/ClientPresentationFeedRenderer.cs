using System;
using PhinixClient.Framework;
using UnityEngine;
using Verse;
using static PhinixClient.ClientPresentationFeedMetrics;

namespace PhinixClient
{
    internal static class ClientPresentationFeedMetrics
    {
            internal static float MediaHeight(ClientPresentationMedia media, float width)
            { return media.State == ClientPresentationMediaState.Ready && media.Image != null ? Mathf.Clamp(width / media.AspectRatio, 48, media.MaximumHeight) : media.State == ClientPresentationMediaState.Failed || media.State == ClientPresentationMediaState.Unavailable ? 32 : 120; }
            internal static float FeedRowHeight(ClientPresentationFeedRow row, float width)
            {
                using (new ClientGuiScope(true))
                {
                Text.Font = GameFont.Small; Text.WordWrap = true;
                float inner = Mathf.Max(1, width - 24 - (row.Indented ? 16 : 0));
                float height = 12 + Text.CalcHeight(row.Body, inner);
                if (!string.IsNullOrEmpty(row.Heading)) height += FeedHeadingHeight(row.Heading, Mathf.Max(1, inner - MetadataWidth(row, inner) - row.MetadataGap)) + 4;
                if (!string.IsNullOrEmpty(row.Reference)) height += Text.CalcHeight(row.Reference, Mathf.Max(1, inner - 12)) + 12;
                foreach (var media in row.Media) height += MediaHeight(media, inner) + 8;
                return height + 6;
                }
            }
            internal static float MetadataWidth(ClientPresentationFeedRow row, float width)
            {
                if (string.IsNullOrEmpty(row.Metadata)) return 0;
                using (new ClientGuiScope(true)) { Text.Font = GameFont.Tiny; Text.WordWrap = false;
                    return Mathf.Min(Mathf.Max(1, width * .4f), Mathf.Max(row.MetadataMinimumWidth, Text.CalcSize(row.Metadata).x + 8)); }
            }
            internal static Rect FeedMetadataRect(ClientPresentationFeedRow row, float x, float y, float width)
            {
                float metadataWidth = MetadataWidth(row, width);
                float offset = Mathf.Max(0, width - metadataWidth);
                if (row.MetadataPlacement == ClientPresentationMetadataPlacement.AfterHeading)
                    using (new ClientGuiScope(true))
                    {
                        Text.Font = GameFont.Small; Text.WordWrap = false;
                        offset = Mathf.Min(offset, Text.CalcSize(row.Heading).x + row.MetadataGap);
                    }
                return new Rect(x + offset, y + 6, metadataWidth, 18);
            }
            internal static float FeedHeadingHeight(string text, float width)
            { using (new ClientGuiScope(true)) { Text.Font = GameFont.Small; Text.WordWrap = true; return Mathf.Max(24, Text.CalcHeight(text, width)) + 4; } }
    }

    // Feed measurement and rendering share one geometry implementation within the same session.
    internal sealed partial class ClientPresentationService
    {
        private sealed partial class Session
        {
            private void DrawFeed(Block block, bool fresh, float viewportHeight)
            {
                var feed = block.Feed;
                var range = VirtualListLayout.GetDynamicRange(block.RowOffsets, feed.Rows.Count, Mathf.Max(0, scroll.y - block.Rect.y),
                    Mathf.Max(0, scroll.y + viewportHeight - block.Rect.y), 1);
                for (int i = range.FirstIndex; i < range.EndIndexExclusive; i++)
                {
                    var row = feed.Rows[i];
                    var rect = new Rect(block.Rect.x, block.Rect.y + block.RowOffsets[i], block.Rect.width, block.RowOffsets[i + 1] - block.RowOffsets[i] - 6);
                    if (row.SurfaceKey != null) ClientWidgets.RoundedSurface(rect, OptionalColor(palette, row.SurfaceKey, palette.Surface.ToUnityColor()));
                    if (feed.Id == highlightedFeed && row.Id == highlightedRow && Time.realtimeSinceStartup < highlightUntil)
                        ClientWidgets.RoundedBorder(rect, palette.GetColor("ui.accent").ToUnityColor());
                    if (Mouse.IsOver(rect)) ClientWidgets.RoundedSurface(rect, OptionalColor(palette, row.HoverKey, palette.HoverHighlight.ToUnityColor()));
                    if (row.AccentKey != null) ClientWidgets.RoundedSurface(new Rect(rect.x, rect.y + 8, 3, Mathf.Max(0, rect.height - 16)), OptionalColor(palette, row.AccentKey, palette.Separator.ToUnityColor()), 1);
                    float x = rect.x + 12 + (row.Indented ? 16 : 0), y = rect.y + 6;
                    float width = Mathf.Max(1, rect.xMax - 12 - x);
                    using (new ClientGuiScope(true))
                    {
                        UnityEngine.GUI.enabled = UnityEngine.GUI.enabled && fresh;
                        if (!string.IsNullOrEmpty(row.Heading))
                        {
                            float height = FeedHeadingHeight(row.Heading, Mathf.Max(1, width - block.MetadataWidths[i] - row.MetadataGap));
                            var heading = new Rect(x, y, Mathf.Max(1, width - block.MetadataWidths[i] - row.MetadataGap), height);
                            using (new ClientGuiScope(true)) { Text.Font = GameFont.Small; ClientWidgets.ClippedLabel(heading, row.Heading, palette.PrimaryText.ToUnityColor(), TextAnchor.UpperLeft, true); }
                            var metadataRect = new Rect(x + block.MetadataOffsets[i], y + 6, block.MetadataWidths[i], Mathf.Max(18, height - 6));
                            var headingHit = heading;
                            if (row.MetadataPlacement == ClientPresentationMetadataPlacement.AfterHeading && !string.IsNullOrEmpty(row.Metadata))
                                headingHit.width = Mathf.Max(1, Mathf.Min(heading.width, metadataRect.x - x - row.MetadataGap));
                            if (Widgets.ButtonInvisible(headingHit) && UnityEngine.GUI.enabled && InputEvent()) Enqueue(feed.Id, ClientPresentationInputKind.FeedHeadingSelected, row.Id);
                            using (new ClientGuiScope(true)) { Text.Font = GameFont.Tiny; ClientWidgets.ClippedLabel(metadataRect, row.Metadata, OptionalColor(palette, row.MetadataColorKey, palette.SecondaryText.ToUnityColor()), TextAnchor.UpperLeft); TooltipHandler.TipRegion(metadataRect, row.Metadata); }
                            y += height + 4;
                        }
                        if (!string.IsNullOrEmpty(row.Reference))
                        {
                            float height = Text.CalcHeight(row.Reference, Mathf.Max(1, width - 12));
                            var reference = new Rect(x + 12, y, Mathf.Max(1, width - 12), height);
                            ClientWidgets.Fill(new Rect(x, y, 2, height), OptionalColor(palette, row.ReferenceBorderKey, palette.Separator.ToUnityColor()));
                            ClientWidgets.Label(reference, row.Reference, palette, true);
                            if (Widgets.ButtonInvisible(reference) && UnityEngine.GUI.enabled && InputEvent()) Enqueue(feed.Id, ClientPresentationInputKind.FeedReferenceSelected, row.Id, row.ReferenceTarget);
                            y += height + 12;
                        }
                        using (new ClientGuiScope(true))
                        {
                            if (row.Centered) Text.Anchor = TextAnchor.MiddleCenter;
                            var body = new Rect(x, y, width, Text.CalcHeight(row.Body, width));
                            ClientWidgets.Label(body, row.Body, OptionalColor(palette, row.BodyColorKey, palette.PrimaryText.ToUnityColor()));
                            if (Widgets.ButtonInvisible(body) && UnityEngine.GUI.enabled && InputEvent()) Enqueue(feed.Id, ClientPresentationInputKind.FeedBodySelected, row.Id);
                            y += body.height;
                        }
                        foreach (var media in row.Media)
                        {
                            y += 8; var mediaRect = new Rect(x, y, width, MediaHeight(media, width)); y += mediaRect.height;
                            if (mediaRect.yMax <= scroll.y || mediaRect.y >= scroll.y + viewportHeight) continue;
                            bool drawn = media.Image != null && media.State == ClientPresentationMediaState.Ready && DrawImage(mediaRect, media.Image);
                            if (!drawn) { ClientWidgets.RoundedSurface(mediaRect, OptionalColor(palette, media.SurfaceKey, palette.Surface.ToUnityColor())); ClientWidgets.Label(mediaRect.ContractedBy(8), media.Text, OptionalColor(palette, media.ColorKey, palette.SecondaryText.ToUnityColor())); }
                            if ((media.State == ClientPresentationMediaState.Idle || media.State == ClientPresentationMediaState.Ready && media.Image == null) && fresh && Event.current?.type == EventType.Repaint && !mediaVisibility.Contains(feed.Id + "/" + row.Id + "/" + media.Id))
                            {
                                // Mark only after delivery. Queue saturation must remain retryable next Repaint.
                                if (Enqueue(feed.Id, ClientPresentationInputKind.FeedMediaVisible, row.Id, media.Id))
                                    mediaVisibility.Add(feed.Id + "/" + row.Id + "/" + media.Id);
                            }
                            if (media.State != ClientPresentationMediaState.Idle && media.State != ClientPresentationMediaState.Loading && Widgets.ButtonInvisible(mediaRect) && UnityEngine.GUI.enabled && InputEvent())
                                Enqueue(feed.Id, ClientPresentationInputKind.FeedMediaSelected, row.Id, media.Id);
                        }
                    }
                }
            }

        }
    }
}
