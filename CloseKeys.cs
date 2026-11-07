namespace MapleBar;

/// <summary>
/// Ctrl+W·Esc는 포커스가 있는 창만 닫는다 (각 창의 ProcessCmdKey와 페이지 스크립트가 처리).
/// 메이플을 하는 중이나 다른 창에 포커스가 있을 때 의도치 않게 닫히지 않도록 전역으로 키를 읽지 않는다.
/// </summary>
static class CloseKeys
{
    public static void Track(Form f) { }
}
