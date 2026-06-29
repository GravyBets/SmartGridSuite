#nullable enable
using System;

namespace SmartGridSuite.Client.Services
{
    public enum ConnectivityState
    {
        Unknown,
        Checking,
        Online,
        Offline,
        Degraded
    }

    public sealed class ConnectivityChangedEventArgs : EventArgs
    {
        public ConnectivityChangedEventArgs(
            ConnectivityState state,
            string message)
        {
            State = state;
            Message = message;
        }

        public ConnectivityState State { get; }

        public string Message { get; }
    }

    public static class ConnectivityService
    {
        private static readonly object SyncRoot = new();

        private static ConnectivityState _currentState =
            ConnectivityState.Unknown;

        private static string _currentMessage = "";

        public static event EventHandler<ConnectivityChangedEventArgs>?
            StateChanged;

        public static ConnectivityState CurrentState
        {
            get
            {
                lock (SyncRoot)
                    return _currentState;
            }
        }

        public static string CurrentMessage
        {
            get
            {
                lock (SyncRoot)
                    return _currentMessage;
            }
        }

        // Marks the beginning of an explicit connection retry.
        public static void BeginCheck()
        {
            SetState(
                ConnectivityState.Checking,
                "Checking connection to Smart Grid Suite...");
        }

        // Marks the API and database as available.
        public static void ReportOnline()
        {
            SetState(
                ConnectivityState.Online,
                "Connected to Smart Grid Suite.");
        }

        // Marks the API as unreachable while allowing cached data to remain visible.
        public static void ReportOffline(string? message = null)
        {
            SetState(
                ConnectivityState.Offline,
                string.IsNullOrWhiteSpace(message)
                    ? "Offline — showing previously loaded data. Server actions are unavailable."
                    : message.Trim());
        }

        // Marks the API as reachable while another required service is unavailable.
        public static void ReportDegraded(string? message = null)
        {
            SetState(
                ConnectivityState.Degraded,
                string.IsNullOrWhiteSpace(message)
                    ? "The server is reachable, but one or more required services are unavailable."
                    : message.Trim());
        }

        // Updates shared connection state and notifies all active application windows.
        private static void SetState(
            ConnectivityState state,
            string message)
        {
            EventHandler<ConnectivityChangedEventArgs>? handler;

            lock (SyncRoot)
            {
                if (_currentState == state &&
                    string.Equals(
                        _currentMessage,
                        message,
                        StringComparison.Ordinal))
                {
                    return;
                }

                _currentState = state;
                _currentMessage = message;

                handler = StateChanged;
            }

            handler?.Invoke(
                null,
                new ConnectivityChangedEventArgs(
                    state,
                    message));
        }
    }
}