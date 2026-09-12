using System;

namespace Effekseer.GUI.Dialog
{
	/// <summary>
	/// ダイアログを開いたまま結果を見せるための土台。
	///
	/// 値を動かすたびに、前回の試し適用を取り消してから新しい値で適用し直す。
	/// 取り消しには履歴から消す指定を使うので、スライダーを何度動かしても
	/// Undo の履歴は汚れず、OK を押した時点の一手だけが残る。
	/// </summary>
	abstract class PreviewDialog : IRemovableControl
	{
		bool opened = true;
		bool isFirstUpdate = true;
		bool hasPreview = false;

		protected Utils.EffectTransformResult LastResult { get; private set; }

		public bool ShouldBeRemoved { get; private set; } = false;

		protected abstract string TitleKey { get; }
		protected abstract string Id { get; }

		/// <summary>
		/// 中身を描く。値が変わったら true を返す。
		/// </summary>
		protected abstract bool DrawContents();

		/// <summary>
		/// 現在の指定で実際に変換する。何もしないときは null を返す。
		/// </summary>
		protected abstract Utils.EffectTransformResult Run();

		/// <summary>
		/// 確定したあとに出す報告の文言。
		/// </summary>
		protected abstract string SkippedMessageKey { get; }

		public void Show()
		{
			Manager.AddControl(this);
		}

		public void Update()
		{
			string title = MultiLanguageTextProvider.GetText(TitleKey) + "###" + Id;

			if (isFirstUpdate)
			{
				Manager.NativeManager.OpenPopup(title);
			}

			if (Manager.NativeManager.BeginPopupModal(title, ref opened, swig.WindowFlags.AlwaysAutoResize))
			{
				if (DrawContents())
				{
					Preview();
				}

				Manager.NativeManager.Separator();

				var result = ModalButtons.OkCancel("###" + Id + "Buttons");

				if (result == ModalResult.Accepted)
				{
					// 試し適用をそのまま残す。取り消していないので一手だけ積まれている。
					Accept();
				}
				else if (result == ModalResult.Cancelled)
				{
					Revert();
				}

				if (result != ModalResult.None) ShouldBeRemoved = true;

				Manager.NativeManager.EndPopup();
			}
			else
			{
				// 枠外を押して閉じた場合も、試し適用は残さない。
				Revert();
				ShouldBeRemoved = true;
			}

			isFirstUpdate = false;
		}

		void Preview()
		{
			Revert();

			LastResult = Run();
			hasPreview = LastResult != null;
		}

		void Revert()
		{
			if (!hasPreview) return;

			Command.CommandManager.Undo(true);
			hasPreview = false;
		}

		void Accept()
		{
			if (LastResult == null) return;

			Commands.ReportTransform(LastResult, SkippedMessageKey);
		}
	}

	/// <summary>
	/// エフェクト全体を平行移動する量を尋ねる。
	/// </summary>
	class DisplaceEffect : PreviewDialog
	{
		readonly float[] offset = new float[] { 0.0f, 0.0f, 0.0f };

		protected override string TitleKey { get { return "InternalDisplace"; } }
		protected override string Id { get { return "DisplaceEffectDialog"; } }
		protected override string SkippedMessageKey { get { return "Transform_Displace_Skipped"; } }

		protected override bool DrawContents()
		{
			Manager.NativeManager.Text(MultiLanguageTextProvider.GetText("Transform_Displace_Desc"));
			Manager.NativeManager.Separator();

			return Manager.NativeManager.DragFloat3("###DisplaceOffset", offset, 1.0f);
		}

		protected override Utils.EffectTransformResult Run()
		{
			if (offset[0] == 0.0f && offset[1] == 0.0f && offset[2] == 0.0f) return null;

			return Utils.EffectTransformer.Displace(Core.Root, offset[0], offset[1], offset[2]);
		}
	}

	/// <summary>
	/// エフェクト全体の大きさの倍率を尋ねる。
	/// </summary>
	class ScaleEffect : PreviewDialog
	{
		readonly float[] factor = new float[] { 1.0f };

		protected override string TitleKey { get { return "InternalScaleEffect"; } }
		protected override string Id { get { return "ScaleEffectDialog"; } }
		protected override string SkippedMessageKey { get { return "Transform_Scale_Skipped"; } }

		protected override bool DrawContents()
		{
			Manager.NativeManager.Text(MultiLanguageTextProvider.GetText("Transform_Scale_Desc"));
			Manager.NativeManager.Separator();

			return Manager.NativeManager.DragFloat(
				MultiLanguageTextProvider.GetText("Transform_Scale_Factor"), factor, 0.01f, 0.01f, 20.0f, "%.2f");
		}

		protected override Utils.EffectTransformResult Run()
		{
			if (factor[0] == 1.0f) return null;

			return Utils.EffectTransformer.Scale(Core.Root, factor[0]);
		}
	}

	/// <summary>
	/// エフェクト全体を回す軸と角度を尋ねる。
	/// </summary>
	class RotateEffect : PreviewDialog
	{
		Utils.TransformAxis axis = Utils.TransformAxis.Y;
		readonly float[] degrees = new float[] { 0.0f };

		protected override string TitleKey { get { return "InternalRotateEffect"; } }
		protected override string Id { get { return "RotateEffectDialog"; } }
		protected override string SkippedMessageKey { get { return "Transform_Rotate_Skipped"; } }

		protected override bool DrawContents()
		{
			Manager.NativeManager.Text(MultiLanguageTextProvider.GetText("Transform_Rotate_Desc"));
			Manager.NativeManager.Separator();

			bool changed = false;
			changed |= AxisButton(Utils.TransformAxis.X, "X###RotateX");
			Manager.NativeManager.SameLine();
			changed |= AxisButton(Utils.TransformAxis.Y, "Y###RotateY");
			Manager.NativeManager.SameLine();
			changed |= AxisButton(Utils.TransformAxis.Z, "Z###RotateZ");

			changed |= Manager.NativeManager.DragFloat(
				MultiLanguageTextProvider.GetText("Transform_Rotate_Angle"), degrees, 1.0f, -180.0f, 180.0f, "%.0f");

			return changed;
		}

		bool AxisButton(Utils.TransformAxis candidate, string label)
		{
			if (!Manager.NativeManager.RadioButton(label, axis == candidate)) return false;
			if (axis == candidate) return false;

			axis = candidate;
			return true;
		}

		protected override Utils.EffectTransformResult Run()
		{
			if (degrees[0] == 0.0f) return null;

			return Utils.EffectTransformer.Rotate(Core.Root, axis, degrees[0]);
		}
	}

	/// <summary>
	/// 色相、彩度、明度の変え方を尋ねる。
	/// </summary>
	class RecolourEffect : PreviewDialog
	{
		Utils.HueMode mode = Utils.HueMode.Set;
		readonly float[] hue = new float[] { 0.0f };
		readonly float[] saturation = new float[] { 1.0f };
		readonly float[] brightness = new float[] { 1.0f };

		protected override string TitleKey { get { return "InternalRecolour"; } }
		protected override string Id { get { return "RecolourEffectDialog"; } }
		protected override string SkippedMessageKey { get { return "Transform_Recolour_Skipped"; } }

		/// <summary>
		/// 選んだ色。色相だけを使い、彩度と明度は倍率の側で決める。
		/// </summary>
		readonly float[] target = new float[] { 1.0f, 0.0f, 0.0f, 1.0f };

		bool hasSample = false;
		readonly float[] sample = new float[] { 1.0f, 1.0f, 1.0f, 1.0f };
		readonly float[] outcome = new float[] { 1.0f, 1.0f, 1.0f, 1.0f };

		protected override bool DrawContents()
		{
			Manager.NativeManager.Text(MultiLanguageTextProvider.GetText("Transform_Recolour_Desc"));
			Manager.NativeManager.Separator();

			bool changed = false;

			if (Manager.NativeManager.RadioButton(
				MultiLanguageTextProvider.GetText("Transform_Recolour_Set"), mode == Utils.HueMode.Set))
			{
				changed |= SetMode(Utils.HueMode.Set);
			}

			Manager.NativeManager.SameLine();

			if (Manager.NativeManager.RadioButton(
				MultiLanguageTextProvider.GetText("Transform_Recolour_Rotate"), mode == Utils.HueMode.Rotate))
			{
				changed |= SetMode(Utils.HueMode.Rotate);
			}

			if (mode == Utils.HueMode.Set)
			{
				// 色を選ばせる。選択見本を押すと色相環と 16 進入力が開く。
				var flags = swig.ColorEditFlags.NoAlpha
					| swig.ColorEditFlags.DisplayHex
					| swig.ColorEditFlags.PickerHueWheel;

				if (Manager.NativeManager.ColorEdit4(
					MultiLanguageTextProvider.GetText("Transform_Recolour_Target"), target, flags))
				{
					float picked;
					if (Utils.EffectRecolour.TryGetHue(target[0], target[1], target[2], out picked))
					{
						hue[0] = picked;
						changed = true;
					}
				}

				Manager.NativeManager.Text(string.Format(
					MultiLanguageTextProvider.GetText("Transform_Recolour_HueReadout"), (int)hue[0]));
			}
			else
			{
				// 相対的な回し幅なので、色を選ばせても意味が通らない。
				changed |= Manager.NativeManager.DragFloat(
					MultiLanguageTextProvider.GetText("Transform_Recolour_Hue"), hue, 1.0f, -180.0f, 180.0f, "%.0f");
			}

			changed |= Manager.NativeManager.DragFloat(
				MultiLanguageTextProvider.GetText("Transform_Recolour_Saturation"), saturation, 0.01f, 0.0f, 4.0f, "%.2f");

			changed |= Manager.NativeManager.DragFloat(
				MultiLanguageTextProvider.GetText("Transform_Recolour_Brightness"), brightness, 0.01f, 0.0f, 4.0f, "%.2f");

			DrawSwatches();

			return changed;
		}

		/// <summary>
		/// エフェクトから拾った色を、変換前と変換後で並べて見せる。
		/// 数字だけでは結果が想像できないため。
		/// </summary>
		void DrawSwatches()
		{
			if (!hasSample)
			{
				float r, g, b;
				if (!Utils.EffectRecolour.TrySampleColour(Core.Root, out r, out g, out b)) return;

				sample[0] = r / 255.0f;
				sample[1] = g / 255.0f;
				sample[2] = b / 255.0f;
				hasSample = true;
			}

			float outR, outG, outB;
			Utils.EffectRecolour.Preview(sample[0] * 255.0f, sample[1] * 255.0f, sample[2] * 255.0f,
				CurrentSettings(), out outR, out outG, out outB);

			outcome[0] = outR / 255.0f;
			outcome[1] = outG / 255.0f;
			outcome[2] = outB / 255.0f;

			Manager.NativeManager.Separator();

			var flags = swig.ColorEditFlags.NoAlpha
				| swig.ColorEditFlags.NoInputs
				| swig.ColorEditFlags.NoPicker
				| swig.ColorEditFlags.NoDragDrop;

			Manager.NativeManager.ColorEdit4(
				MultiLanguageTextProvider.GetText("Transform_Recolour_Before"), sample, flags);

			Manager.NativeManager.SameLine();

			Manager.NativeManager.ColorEdit4(
				MultiLanguageTextProvider.GetText("Transform_Recolour_After"), outcome, flags);
		}

		Utils.RecolourSettings CurrentSettings()
		{
			var settings = Utils.RecolourSettings.Default;
			settings.Mode = mode;
			settings.Hue = hue[0];
			settings.Saturation = saturation[0];
			settings.Brightness = brightness[0];
			return settings;
		}

		protected override Utils.EffectTransformResult Run()
		{
			var settings = CurrentSettings();
			if (settings.IsIdentity) return null;

			return Utils.EffectRecolour.Apply(Core.Root, settings);
		}

		bool SetMode(Utils.HueMode next)
		{
			if (mode == next) return false;

			mode = next;

			// 指定は色相環の位置、回転は前後の振れ幅。範囲が違うので移すときに畳む。
			if (next == Utils.HueMode.Rotate)
			{
				hue[0] = Math.Max(-180.0f, Math.Min(180.0f, hue[0]));
			}
			else
			{
				hue[0] = (hue[0] % 360.0f + 360.0f) % 360.0f;
			}

			return true;
		}
	}

	enum ModalResult
	{
		None,
		Accepted,
		Cancelled,
	}

	/// <summary>
	/// OK と取り消しのボタン。押された結果をその場で返すので、
	/// ダイアログが同時に開いても混ざらない。
	/// </summary>
	static class ModalButtons
	{
		public static ModalResult OkCancel(string id)
		{
			float dpiScale = Manager.DpiScale;
			float buttonWidth = 80 * dpiScale;
			float areaWidth = Manager.NativeManager.GetContentRegionAvail().X;

			Manager.NativeManager.SetCursorPosX((areaWidth - buttonWidth * 2 + 8 * dpiScale) / 2);

			var result = ModalResult.None;

			// 「###」以降が識別子になる。両方に同じものを付けると衝突するので、
			// ボタンごとに違う末尾を足す。
			if (Manager.NativeManager.Button("OK" + id + "Ok", buttonWidth))
			{
				result = ModalResult.Accepted;
			}

			Manager.NativeManager.SameLine();

			if (Manager.NativeManager.Button(
				MultiLanguageTextProvider.GetText("Cancel") + id + "Cancel", buttonWidth))
			{
				result = ModalResult.Cancelled;
			}

			if (Manager.NativeManager.IsKeyPressed(Manager.NativeManager.GetKeyIndex(swig.Key.Escape)))
			{
				result = ModalResult.Cancelled;
			}

			return result;
		}
	}
}
