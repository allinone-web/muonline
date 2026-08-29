using MuAssets.Core;

namespace Client.MapEditor;

/// <summary>
/// <c>--selftest</c> 的宿主端包裝。斷言本身在 <see cref="EditPipelineSelfTest"/>（Core）。
/// </summary>
/// <remarks>
/// 這裡多做的只有一件事：把相機帶到剛剛畫過的地方，
/// 這樣配合 <c>--screenshot</c> 才驗證得到「改動有沒有真的推進渲染端」——
/// 那是無頭版測不到、只有跑在引擎裡才測得到的部分。
/// </remarks>
public static class EditorSelfTest
{
    public static bool Run(EditorSession session, MapEditorScene scene)
    {
        bool passed = EditPipelineSelfTest.Run(session, session.LoadedWorld);

        session.Camera.Mode = CameraMode.Orbit;
        session.Camera.Distance = 3000f;
        session.Camera.FocusTile(EditPipelineSelfTest.OriginX + 10, EditPipelineSelfTest.OriginY + 10);

        return passed;
    }
}
