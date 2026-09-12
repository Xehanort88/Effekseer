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
	/// 色相、彩度、明度の変え方を尋ねる。
	/// </summary>
	class RecolourEffect : PreviewDialog
	{
		Utils.HueMode mode = Utils.HueMode.Rotate;
		readonly float[] hue = new float[] { 0.0f };
		readonly float[] saturation = new float[] { 1.0f };
		readonly float[] brightness = new float[] { 1.0f };

		protected override string TitleKey { get { return "InternalRecolour"; } }
		protected override string Id { get { return "RecolourEffectDialog"; } }
		protected override string SkippedMessageKey { get { return "Transform_Recolour_Skipped"; } }

		protected override bool DrawContents()
		{
			Manager.NativeManager.Text(MultiLanguageTextProvider.GetText("Transform_Recolour_Desc"));
			Manager.NativeManager.Separator();

			bool changed = false;

			if (Manager.NativeManager.RadioButton(
				MultiLanguageTextProvider.GetText("Transform_Recolour_Rotate"), mode == Utils.HueMode.Rotate))
			{
				changed |= SetMode(Utils.HueMode.Rotate);
			}

			Manager.NativeManager.SameLine();

			if (Manager.NativeManager.RadioButton(
				MultiLanguageTextProvider.GetText("Transform_Recolour_Set"), mode == Utils.HueMode.Set))
			{
				changed |= SetMode(Utils.HueMode.Set);
			}

			// 回転は前後どちらにも回せるが、指定は色相環の位置そのものになる。
			float minimum = mode == Utils.HueMode.Rotate ? -180.0f : 0.0f;
			float maximum = mode == Utils.HueMode.Rotate ? 180.0f : 360.0f;

			changed |= Manager.NativeManager.DragFloat(
				MultiLanguageTextProvider.GetText("Transform_Recolour_Hue"), hue, 1.0f, minimum, maximum, "%.0f");

			changed |= Manager.NativeManager.DragFloat(
				MultiLanguageTextProvider.GetText("Transform_Recolour_Saturation"), saturation, 0.01f, 0.0f, 4.0f, "%.2f");

			changed |= Manager.NativeManager.DragFloat(
				MultiLanguageTextProvider.GetText("Transform_Recolour_Brightness"), brightness, 0.01f, 0.0f, 4.0f, "%.2f");

			return changed;
		}

		protected override Utils.EffectTransformResult Run()
		{
			var settings = Utils.RecolourSettings.Default;
			settings.Mode = mode;
			settings.Hue = hue[0];
			settings.Saturation = saturation[0];
			settings.Brightness = brightness[0];

			if (settings.IsIdentity) return null;

			return Utils.EffectRecolour.Apply(Core.Root, settings);
		}

		bool SetMode(Utils.HueMode next)
		{
			if (mode == next) return false;

			mode = next;

			// 片方の範囲に収まらない値が残ると、意図しない色になる。
			hue[0] = next == Utils.HueMode.Rotate
				? Math.Max(-180.0f, Math.Min(180.0f, hue[0]))
				: Math.Max(0.0f, Math.Min(360.0f, hue[0]));

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

			if (Manager.NativeManager.Button("OK" + id, buttonWidth))
			{
				result = ModalResult.Accepted;
			}

			Manager.NativeManager.SameLine();

			if (Manager.NativeManager.Button(MultiLanguageTextProvider.GetText("Cancel") + id, buttonWidth))
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
