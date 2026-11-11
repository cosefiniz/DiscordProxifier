using System.Windows;

namespace DiscordUnblocker.Glass
{
    // PLACEHOLDER — replaced by glass alert sheets.
    public static class GlassDialog
    {
        public static bool Confirm(Window owner, string title, string body)
        {
            return MessageBox.Show(owner, body, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        public static string PickFolder(Window owner, string description, string startPath)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = description, ShowNewFolderButton = false, SelectedPath = startPath })
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
        }
    }
}
