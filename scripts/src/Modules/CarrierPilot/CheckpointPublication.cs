
void PublishInternalState(string core, string route, string reports)
{
    // Build the replacement first; allocation failure must leave the old complete
    // Storage and its cached base untouched.
    core = AddCapturedModuleState(core);
    string next = EncodeInternalState(core, route, reports, "");
    Storage = next;
    _internalCore = core; _internalRoute = route; _internalReports = reports; _internalProgress = "";
}


bool PublishInternalProgress(string progress)
{
    if (string.IsNullOrWhiteSpace(_internalCore))
    {
        // Never publish an overlay without its complete base. This also handles
        // a newly placed PB before its first full checkpoint has completed.
        RequestCarrierSave(true);
        return false;
    }
    progress = AddCapturedModuleState(progress);
    string next = EncodeInternalState(_internalCore, _internalRoute, _internalReports, progress);
    if (!string.Equals(Storage, next, StringComparison.Ordinal)) Storage = next;
    _internalProgress = progress;
    return true;
}

string stateIniTimerTag { get { return Ca("INI Pilot Core"); } }
string stateRouteTimerTag { get { return Ca("INI Pilot Route"); } }
string stateReportsTimerTag { get { return Ca("INI Pilot Reports"); } }
