using UnityEngine;
using UnityEngine.UIElements;

namespace RunBeat
{
    public sealed class BeatRing : VisualElement
    {
        public float phase;
        public bool playing;
        public BeatRing() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D;
            Vector2 center = contentRect.center; float radius = Mathf.Min(contentRect.width, contentRect.height) * .46f;
            p.strokeColor = new Color32(49, 53, 54, 255); p.lineWidth = 4;
            Circle(p, center, radius, false);
            int active = Mathf.FloorToInt(phase * 12) % 12;
            for (int i = 0; i < 12; i++)
            {
                float angle = (i * 30 - 90) * Mathf.Deg2Rad;
                var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius - 16);
                bool on = playing && i == active;
                if (on) { p.fillColor = new Color(0.76f, 1, .32f, .08f); Circle(p, point, 16, true); p.fillColor = new Color(0.76f, 1, .32f, .16f); Circle(p, point, 11, true); }
                p.fillColor = on ? new Color32(195, 255, 84, 255) : new Color32(78, 83, 85, 255);
                Circle(p, point, on ? 7 : 5, true);
            }
        }
        internal static void Circle(Painter2D p, Vector2 c, float r, bool fill)
        {
            p.BeginPath();
            for (int i = 0; i <= 64; i++) { float a = i * Mathf.PI / 32; var v = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; if (i == 0) p.MoveTo(v); else p.LineTo(v); }
            p.ClosePath(); if (fill) p.Fill(); else p.Stroke();
        }
    }
    public sealed class RunIcon : VisualElement
    {
        readonly string kind; readonly Color color;
        public RunIcon(string kind, Color? tint = null, int size = 24)
        {
            this.kind = kind; color = tint ?? new Color32(241, 242, 243, 255);
            style.width = size; style.height = size; style.minWidth = size; pickingMode = PickingMode.Ignore; generateVisualContent += Draw;
        }
        void Draw(MeshGenerationContext context)
        {
            var p = context.painter2D; p.strokeColor = color; p.fillColor = color; p.lineWidth = 1.8f;
            float scale = contentRect.width / 24;
            Vector2 V(float x, float y) => new Vector2(x * scale, y * scale);
            void Line(params float[] points) { p.BeginPath(); p.MoveTo(V(points[0], points[1])); for (int i = 2; i < points.Length; i += 2) p.LineTo(V(points[i], points[i + 1])); p.Stroke(); }
            void Poly(params float[] points) { p.BeginPath(); p.MoveTo(V(points[0], points[1])); for (int i = 2; i < points.Length; i += 2) p.LineTo(V(points[i], points[i + 1])); p.ClosePath(); p.Fill(); }
            void Dot(float x, float y, float r) => BeatRing.Circle(p, V(x, y), r * scale, true);
            switch (kind)
            {
                case "plus": Line(5, 12, 19, 12); Line(12, 5, 12, 19); break;
                case "minus": Line(5, 12, 19, 12); break;
                case "back": Line(15, 4, 7, 12, 15, 20); break;
                case "chevron": Line(9, 6, 15, 12, 9, 18); break;
                case "close": Line(6, 6, 18, 18); Line(6, 18, 18, 6); break;
                case "play": Poly(7, 4, 20, 12, 7, 20); break;
                case "pause": Poly(5, 4, 10, 4, 10, 20, 5, 20); Poly(14, 4, 19, 4, 19, 20, 14, 20); break;
                case "stop": Poly(5, 5, 19, 5, 19, 19, 5, 19); break;
                case "check": Line(4, 12, 10, 18, 21, 6); break;
                case "clock": BeatRing.Circle(p, V(12, 14), 8 * scale, false); Line(9, 2, 15, 2); Line(12, 2, 12, 5); Line(12, 9, 12, 14, 15, 15); Line(18, 5, 20, 7); break;
                case "history": BeatRing.Circle(p, V(12, 12), 8 * scale, false); Line(12, 7, 12, 12, 16, 14); Line(2, 4, 2, 9, 7, 9); break;
                case "music": Line(9, 17, 9, 5, 20, 3, 20, 15); Line(9, 8, 20, 6); Dot(6, 18, 3); Dot(17, 16, 3); break;
                case "sound": Line(4, 9, 8, 9, 13, 5, 13, 19, 8, 15, 4, 15, 4, 9); Line(16, 8, 18, 10, 18, 14, 16, 16); Line(19, 5, 22, 9, 22, 15, 19, 19); break;
                case "settings": BeatRing.Circle(p, V(12, 12), 7 * scale, false); BeatRing.Circle(p, V(12, 12), 2.5f * scale, false); for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4; Line(12 + Mathf.Cos(a) * 7, 12 + Mathf.Sin(a) * 7, 12 + Mathf.Cos(a) * 10, 12 + Mathf.Sin(a) * 10); } break;
                case "run": Dot(15, 4, 2.2f); Line(5, 10, 9, 7, 13, 8, 16, 12, 21, 12); Line(13, 8, 10, 14, 15, 17, 14, 22); Line(10, 14, 7, 19, 2, 19); break;
                case "shoe": Poly(3, 14, 6, 12, 12, 4, 15, 6, 15, 10, 18, 10, 21, 7, 23, 13, 20, 17, 12, 21, 4, 21, 1, 19, 1, 16); p.strokeColor = new Color32(19, 21, 22, 255); Line(7, 11, 10, 13); Line(9, 8, 12, 10); p.strokeColor = Color.white; Line(2, 19, 9, 20, 20, 16, 23, 13); break;
                case "heart": Line(12, 21, 3, 12, 2, 8, 4, 4, 8, 3, 12, 7, 16, 3, 20, 4, 22, 8, 21, 12, 12, 21); break;
                case "youtube": Poly(3, 5, 21, 5, 23, 7, 23, 17, 21, 19, 3, 19, 1, 17, 1, 7); p.fillColor = new Color32(49, 52, 55, 255); Poly(10, 8, 16, 12, 10, 16); break;
                case "bulb": BeatRing.Circle(p, V(12, 9), 6 * scale, false); Line(8, 14, 9, 18, 15, 18, 16, 14); Line(10, 21, 14, 21); break;
            }
        }
    }
}
