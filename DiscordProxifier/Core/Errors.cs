using System;

namespace DiscordUnblocker
{
    public enum ErrorAction
    {
        Retry,
        SwitchMode,
        OpenSettings,
        AddBridges,
        ChangeEndpoint,
        KillLeftovers,
        OpenLogs,
        OpenFolder,
        BrowseDiscord,
        RescanDiscord,
        RetryDll,
        NewCircuit,
        InstallUpdate,
        ReleaseNotes,
        Repair
    }

    // A user-facing problem: a stable code (DP-xxx), an argument for the localized
    // description, and the one-click fixes the error card offers.
    public class AppError
    {
        public string Code;
        public string Arg;
        public ErrorAction[] Actions;

        // Info cards (e.g. an available update) use the accent colour and show the argument
        // instead of a DP code.
        public bool IsInfo;

        public string Title => string.Format(L.T(Code + ".t"), Arg ?? "");
        public string DisplayCode => IsInfo ? (string.IsNullOrEmpty(Arg) ? "" : "v" + Arg) : Code;
        public string Description => string.Format(L.T(Code + ".d"), Arg ?? "");

        public AppError(string code, string arg, params ErrorAction[] actions)
        {
            Code = code;
            Arg = arg;
            Actions = actions;
        }

        public static AppError For(string code, string arg = null)
        {
            switch (code)
            {
                case "DP-101":
                    return new AppError(code, arg, ErrorAction.KillLeftovers, ErrorAction.OpenSettings);
                case "DP-102":
                    return new AppError(code, arg, ErrorAction.Retry, ErrorAction.OpenLogs);
                case "DP-103":
                    return new AppError(code, arg, ErrorAction.OpenFolder, ErrorAction.Retry);
                case "DP-201":
                    return new AppError(code, arg, ErrorAction.AddBridges, ErrorAction.SwitchMode);
                case "DP-202":
                    return new AppError(code, arg, ErrorAction.Retry, ErrorAction.OpenLogs);
                case "DP-301":
                    return new AppError(code, arg, ErrorAction.SwitchMode, ErrorAction.Retry);
                case "DP-302":
                    return new AppError(code, arg, ErrorAction.Retry, ErrorAction.SwitchMode);
                case "DP-303":
                    return new AppError(code, arg, ErrorAction.ChangeEndpoint, ErrorAction.SwitchMode);
                case "DP-401":
                    return new AppError(code, arg, ErrorAction.Retry, ErrorAction.SwitchMode);
                case "DP-402":
                    return new AppError(code, arg, ErrorAction.NewCircuit, ErrorAction.SwitchMode);
                case "DP-700":
                    return new AppError(code, arg, ErrorAction.Retry, ErrorAction.AddBridges, ErrorAction.OpenLogs);
                case "DP-601":
                    return new AppError(code, arg, ErrorAction.SwitchMode, ErrorAction.Retry);
                case "DP-602":
                    return new AppError(code, arg, ErrorAction.SwitchMode, ErrorAction.Retry);
                case "UPDATE":
                    return new AppError(code, arg, ErrorAction.InstallUpdate, ErrorAction.ReleaseNotes) { IsInfo = true };
                case "DP-501":
                    return new AppError(code, arg, ErrorAction.BrowseDiscord, ErrorAction.Repair);
                case "DP-502":
                    return new AppError(code, arg, ErrorAction.Repair, ErrorAction.OpenLogs);
                case "DP-503":
                    return new AppError(code, arg, ErrorAction.Repair, ErrorAction.OpenLogs);
                default:
                    return new AppError(code, arg, ErrorAction.Retry, ErrorAction.OpenLogs);
            }
        }
    }

    // Thrown inside the proxy engine to carry an error code up to the UI.
    public class ProxyFailure : Exception
    {
        public readonly string Code;
        public readonly string Arg;

        public ProxyFailure(string code, string arg = null, string logMessage = null)
            : base(logMessage ?? code + (arg != null ? ": " + arg : ""))
        {
            Code = code;
            Arg = arg;
        }
    }
}
