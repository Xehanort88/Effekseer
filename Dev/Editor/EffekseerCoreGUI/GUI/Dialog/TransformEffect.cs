using System;

namespace Effekseer.GUI.Dialog
{
	/// <summary>
	/// エフェクト全体を平行移動する量を尋ねる。
	/// </summary>
	class DisplaceEffect : IRemovableControl
	{
		bool opened = true;
		bool isFirstUpdate = true;

		public bool ShouldBeRemoved { get; private set; } = false;

		readonly float[] offset = new float[] { 0.0f, 0.0f, 0.0f };

		public void Show()
		{
			Manager.AddControl(this);
		}

		public void Update()
		{
			string title = MultiLanguageTextProvider.GetText("InternalDisplace") + "###DisplaceEffectDialog";

			if (isFirstUpdate)
			{
				Manager.NativeManager.OpenPopup(title);
			}

			if (Manager.NativeManager.BeginPopupModal(title, ref opened, swig.WindowFlags.AlwaysAutoResize))
			{
				Manager.NativeManager.Text(MultiLanguageTextProvider.GetText("Transform_Displace_Desc"));
				Manager.NativeManager.Separator();

				Manager.NativeManager.DragFloat3("###DisplaceOffset", offset, 1.0f);

				Manager.NativeManager.Separator();

				var result = ModalButtons.OkCancel("###DisplaceButtons");

				if (result == ModalResult.Accepted)
				{
					Commands.ApplyDisplace(offset[0], offset[1], offset[2]);
				}

				if (result != ModalResult.None) ShouldBeRemoved = true;

				Manager.NativeManager.EndPopup();
			}
			else
			{
				ShouldBeRemoved = true;
			}

			isFirstUpdate = false;
		}
	}

	/// <summary>
	/// 色相の変え方を尋ねる。彩度と明度はそのまま残る。
	/// </summary>
	class RecolourEffect : IRemovableControl
	{
		bool opened = true;
		bool isFirstUpdate = true;

		public bool ShouldBeRemoved { get; private set; } = false;

		Utils.HueMode mode = Utils.HueMode.Rotate;
		readonly float[] hue = new float[] { 0.0f };

		public void Show()
		{
			Manager.AddControl(this);
		}

		public void Update()
		{
			string title = MultiLanguageTextProvider.GetText("InternalRecolour") + "###RecolourEffectDialog";

			if (isFirstUpdate)
			{
				Manager.NativeManager.OpenPopup(title);
			}

			if (Manager.NativeManager.BeginPopupModal(title, ref opened, swig.WindowFlags.AlwaysAutoResize))
			{
				Manager.NativeManager.Text(MultiLanguageTextProvider.GetText("Transform_Recolour_Desc"));
				Manager.NativeManager.Separator();

				if (Manager.NativeManager.RadioButton(
					MultiLanguageTextProvider.GetText("Transform_Recolour_Rotate"), mode == Utils.HueMode.Rotate))
				{
					SetMode(Utils.HueMode.Rotate);
				}

				Manager.NativeManager.SameLine();

				if (Manager.NativeManager.RadioButton(
					MultiLanguageTextProvider.GetText("Transform_Recolour_Set"), mode == Utils.HueMode.Set))
				{
					SetMode(Utils.HueMode.Set);
				}

				// 回転は前後どちらにも回せるが、指定は色相環の位置そのものになる。
				float minimum = mode == Utils.HueMode.Rotate ? -180.0f : 0.0f;
				float maximum = mode == Utils.HueMode.Rotate ? 180.0f : 360.0f;

				Manager.NativeManager.DragFloat(
					MultiLanguageTextProvider.GetText("Transform_Recolour_Hue"), hue, 1.0f, minimum, maximum, "%.0f");

				Manager.NativeManager.Separator();

				var result = ModalButtons.OkCancel("###RecolourButtons");

				if (result == ModalResult.Accepted)
				{
					Commands.ApplyRecolour(mode, hue[0]);
				}

				if (result != ModalResult.None) ShouldBeRemoved = true;

				Manager.NativeManager.EndPopup();
			}
			else
			{
				ShouldBeRemoved = true;
			}

			isFirstUpdate = false;
		}

		void SetMode(Utils.HueMode next)
		{
			if (mode == next) return;

			mode = next;

			// 片方の範囲に収まらない値が残ると、意図しない色になる。
			hue[0] = next == Utils.HueMode.Rotate
				? Math.Max(-180.0f, Math.Min(180.0f, hue[0]))
				: Math.Max(0.0f, Math.Min(360.0f, hue[0]));
		}
	}

	/// <summary>
	/// OK と取り消しのボタン。両方のダイアログで同じ並びを使う。
	/// 押された結果をその場で返すので、ダイアログが同時に開いても混ざらない。
	/// </summary>
	enum ModalResult
	{
		None,
		Accepted,
		Cancelled,
	}

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
