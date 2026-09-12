using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Effekseer.Data;
using Effekseer.Data.Value;

namespace Effekseer.Utils
{
	/// <summary>
	/// 色相の変え方
	/// </summary>
	public enum HueMode : int
	{
		/// <summary>
		/// すべての色相を同じ角度だけ回す。色どうしの違いは保たれる。
		/// </summary>
		Rotate = 0,

		/// <summary>
		/// すべての色相を同じ値にする。
		/// </summary>
		Set = 1,
	}

	/// <summary>
	/// 描画設定の色を、彩度と明度を変えずに塗り替える。
	///
	/// 対象となる色の型は Color, ColorWithRandom, Gradient, FCurveColorRGBA の 4 つだけで、
	/// それらは描画設定のあちこちに 50 箇所以上ある。ひとつずつ書き並べると
	/// 追加や改名のたびに漏れるので、描画設定を反射で歩いて見つける。
	/// </summary>
	public static class EffectRecolour
	{
		/// <summary>
		/// HSVA 色空間での色相の目盛り。RGBHSVColor が 360 度をこの値に写している。
		/// </summary>
		const int HueScale = 252;

		public static EffectTransformResult Apply(NodeRoot root, HueMode mode, float hueDegrees)
		{
			var result = new EffectTransformResult();
			if (root == null) return result;

			Command.CommandManager.StartCollection();
			try
			{
				foreach (var node in EnumerateNodes(root))
				{
					Walk(node.DrawingValues, mode, hueDegrees, new HashSet<object>(ReferenceComparer.Instance), 0);
					result.NodeCount++;
				}
			}
			finally
			{
				Command.CommandManager.EndCollection();
			}

			return result;
		}

		static IEnumerable<Node> EnumerateNodes(NodeBase parent)
		{
			for (int i = 0; i < parent.Children.Count; i++)
			{
				var child = parent.Children[i];
				yield return child;

				foreach (var descendant in EnumerateNodes(child))
				{
					yield return descendant;
				}
			}
		}

		#region traversal

		/// <summary>
		/// 描画設定の中を下りながら、色の型に行き当たったら塗り替える。
		/// </summary>
		static void Walk(object target, HueMode mode, float hueDegrees, HashSet<object> visited, int depth)
		{
			if (target == null) return;
			if (depth > 8) return;
			if (!visited.Add(target)) return;

			if (target is Data.Value.Color)
			{
				Recolour((Data.Value.Color)target, mode, hueDegrees);
				return;
			}

			if (target is ColorWithRandom)
			{
				Recolour((ColorWithRandom)target, mode, hueDegrees);
				return;
			}

			if (target is Gradient)
			{
				Recolour((Gradient)target, mode, hueDegrees);
				return;
			}

			if (target is FCurveColorRGBA)
			{
				Recolour((FCurveColorRGBA)target, mode, hueDegrees);
				return;
			}

			foreach (var child in ChildrenOf(target))
			{
				Walk(child, mode, hueDegrees, visited, depth + 1);
			}
		}

		/// <summary>
		/// エフェクトのデータ型だけを辿る。文字列やパスの中に色はない。
		/// </summary>
		static IEnumerable<object> ChildrenOf(object target)
		{
			var type = target.GetType();

			foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
			{
				if (property.GetIndexParameters().Length > 0) continue;
				if (!IsWorthVisiting(property.PropertyType)) continue;

				object value;
				try
				{
					value = property.GetValue(target, null);
				}
				catch (TargetInvocationException)
				{
					continue;
				}

				if (value != null) yield return value;
			}

			foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
			{
				if (!IsWorthVisiting(field.FieldType)) continue;

				var value = field.GetValue(target);
				if (value != null) yield return value;
			}
		}

		static bool IsWorthVisiting(Type type)
		{
			if (type.IsPrimitive || type.IsEnum) return false;
			if (type == typeof(string)) return false;

			var space = type.Namespace;
			return space != null && space.StartsWith("Effekseer.Data", StringComparison.Ordinal);
		}

		#endregion

		#region colour maths

		/// <summary>
		/// 彩度と明度を保ったまま色相だけを差し替える。
		/// 灰色は色相を持たないので、純白も純黒も中間の灰色も対象外になる。
		/// 変換したときは true を返す。
		/// </summary>
		static bool TryRecolour(double r, double g, double b, HueMode mode, float hueDegrees,
			out double outR, out double outG, out double outB)
		{
			outR = r;
			outG = g;
			outB = b;

			double max = Math.Max(r, Math.Max(g, b));
			double min = Math.Min(r, Math.Min(g, b));

			// 彩度ゼロ。白、黒、灰色はここで弾かれる。
			if (max <= 0.0 || max == min) return false;

			double chroma = max - min;

			double hue;
			if (max == r) hue = 60.0 * (g - b) / chroma;
			else if (max == g) hue = 60.0 * (b - r) / chroma + 120.0;
			else hue = 60.0 * (r - g) / chroma + 240.0;

			hue = Wrap(mode == HueMode.Set ? hueDegrees : hue + hueDegrees, 360.0);

			double saturation = chroma / max;
			double value = max;

			HsvToRgb(hue, saturation, value, out outR, out outG, out outB);
			return true;
		}

		static void HsvToRgb(double hue, double saturation, double value,
			out double r, out double g, out double b)
		{
			double sector = hue / 60.0;
			int index = (int)Math.Floor(sector) % 6;
			if (index < 0) index += 6;

			double fraction = sector - Math.Floor(sector);
			double p = value * (1.0 - saturation);
			double q = value * (1.0 - saturation * fraction);
			double t = value * (1.0 - saturation * (1.0 - fraction));

			switch (index)
			{
				case 0: r = value; g = t; b = p; break;
				case 1: r = q; g = value; b = p; break;
				case 2: r = p; g = value; b = t; break;
				case 3: r = p; g = q; b = value; break;
				case 4: r = t; g = p; b = value; break;
				default: r = value; g = p; b = q; break;
			}
		}

		static double Wrap(double value, double period)
		{
			double wrapped = value % period;
			return wrapped < 0.0 ? wrapped + period : wrapped;
		}

		static int Round(double value)
		{
			return (int)Math.Round(value, MidpointRounding.AwayFromZero);
		}

		#endregion

		#region colour values

		static void Recolour(Data.Value.Color color, HueMode mode, float hueDegrees)
		{
			if (color.ColorSpace == ColorSpace.HSVA)
			{
				// この色空間では R が色相、G が彩度を保持している。
				if (color.G.Value == 0) return;

				color.R.SetValue(ShiftHsvaHue(color.R.Value, mode, hueDegrees));
				return;
			}

			double r, g, b;
			if (!TryRecolour(color.R.Value, color.G.Value, color.B.Value, mode, hueDegrees, out r, out g, out b))
			{
				return;
			}

			color.SetValue(Round(r), Round(g), Round(b), color.A.Value);
		}

		static void Recolour(ColorWithRandom color, HueMode mode, float hueDegrees)
		{
			if (color.ColorSpace == ColorSpace.HSVA)
			{
				RecolourHsvaChannel(color.R, color.G, mode, hueDegrees);
				return;
			}

			// 乱数の下限と上限はそれぞれひとつの色なので、別々に塗り替える。
			RecolourTriple(color.R, color.G, color.B, mode, hueDegrees, true);
			RecolourTriple(color.R, color.G, color.B, mode, hueDegrees, false);
		}

		static void RecolourTriple(IntWithRandom r, IntWithRandom g, IntWithRandom b,
			HueMode mode, float hueDegrees, bool useMin)
		{
			int oldR = useMin ? r.GetMin() : r.GetMax();
			int oldG = useMin ? g.GetMin() : g.GetMax();
			int oldB = useMin ? b.GetMin() : b.GetMax();

			double newR, newG, newB;
			if (!TryRecolour(oldR, oldG, oldB, mode, hueDegrees, out newR, out newG, out newB)) return;

			if (useMin)
			{
				r.SetMin(Round(newR));
				g.SetMin(Round(newG));
				b.SetMin(Round(newB));
			}
			else
			{
				r.SetMax(Round(newR));
				g.SetMax(Round(newG));
				b.SetMax(Round(newB));
			}
		}

		static void RecolourHsvaChannel(IntWithRandom hue, IntWithRandom saturation,
			HueMode mode, float hueDegrees)
		{
			if (saturation.GetMax() > 0) hue.SetMax(ShiftHsvaHue(hue.GetMax(), mode, hueDegrees));
			if (saturation.GetMin() > 0) hue.SetMin(ShiftHsvaHue(hue.GetMin(), mode, hueDegrees));
		}

		static int ShiftHsvaHue(int hue, HueMode mode, float hueDegrees)
		{
			double scaled = hueDegrees / 360.0 * HueScale;
			double shifted = mode == HueMode.Set ? scaled : hue + scaled;

			return Round(Wrap(shifted, HueScale));
		}

		static void Recolour(Gradient gradient, HueMode mode, float hueDegrees)
		{
			var state = gradient.GetValue();
			if (state == null || state.ColorMarkers == null) return;

			var markers = new Gradient.ColorMarker[state.ColorMarkers.Length];
			bool changed = false;

			for (int i = 0; i < markers.Length; i++)
			{
				var marker = state.ColorMarkers[i];

				double r, g, b;
				if (TryRecolour(marker.ColorR, marker.ColorG, marker.ColorB, mode, hueDegrees, out r, out g, out b))
				{
					marker.ColorR = (float)r;
					marker.ColorG = (float)g;
					marker.ColorB = (float)b;
					changed = true;
				}

				markers[i] = marker;
			}

			if (!changed) return;

			var next = (Gradient.State)state.Clone();
			next.ColorMarkers = markers;
			gradient.SetValue(next);
		}

		/// <summary>
		/// 赤、緑、青が別々のカーブなので、色をひとつ取り出すには 3 本を同じ
		/// フレームで読む必要がある。どれかがキーを持つフレームをすべて集め、
		/// そこで色を作り直す。キーの間は再補間されるため、曲線の形は少し変わる。
		/// </summary>
		static void Recolour(FCurveColorRGBA curve, HueMode mode, float hueDegrees)
		{
			var frames = curve.R.Keys.Concat(curve.G.Keys).Concat(curve.B.Keys)
				.Select(key => key.Frame)
				.Distinct()
				.OrderBy(frame => frame)
				.ToArray();

			if (frames.Length == 0) return;

			var red = new int[frames.Length];
			var green = new int[frames.Length];
			var blue = new int[frames.Length];
			bool changed = false;

			for (int i = 0; i < frames.Length; i++)
			{
				int frame = frames[i];
				double oldR = curve.R.GetValue(frame);
				double oldG = curve.G.GetValue(frame);
				double oldB = curve.B.GetValue(frame);

				double r, g, b;
				if (!TryRecolour(oldR, oldG, oldB, mode, hueDegrees, out r, out g, out b))
				{
					r = oldR;
					g = oldG;
					b = oldB;
				}
				else
				{
					changed = true;
				}

				red[i] = Round(r);
				green[i] = Round(g);
				blue[i] = Round(b);
			}

			if (!changed) return;

			SetLinearKeys(curve.R, frames, red);
			SetLinearKeys(curve.G, frames, green);
			SetLinearKeys(curve.B, frames, blue);
		}

		static void SetLinearKeys(FCurve<int> curve, int[] frames, int[] values)
		{
			var keys = new FCurveKey<int>[frames.Length];

			for (int i = 0; i < frames.Length; i++)
			{
				var key = new FCurveKey<int>(frames[i], values[i]);
				key.SetInterpolationType(FCurveInterpolation.Linear);
				keys[i] = key;
			}

			curve.SetKeys(keys);
		}

		#endregion

		/// <summary>
		/// 同じ実体を二度歩かないための比較子。値の等価性で畳まれると困る。
		/// </summary>
		class ReferenceComparer : IEqualityComparer<object>
		{
			public static readonly ReferenceComparer Instance = new ReferenceComparer();

			public new bool Equals(object x, object y)
			{
				return ReferenceEquals(x, y);
			}

			public int GetHashCode(object obj)
			{
				return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
			}
		}
	}
}
