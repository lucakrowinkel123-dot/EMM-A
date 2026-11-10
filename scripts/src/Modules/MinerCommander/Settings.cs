bool HandleCommanderSetting(string trimmed)
{
    string key=trimmed.Split(' ')[0].ToUpperInvariant();
    if(key=="TOFFRESET") { TOFFR=0;TOFFU=0;TOFFF=5;PublishMinerPlannerSettings();SaveState();PublishMinerSettings();return true; }
    if(key!="SPEEDF"&&key!="SPEEDB"&&key!="MINEH"&&key!="MINEW"&&key!="AUTOSERVICE"
        &&key!="AUTOCARGO"&&key!="AUTOHYDRO"&&key!="ROUTESTARTEXTRA"&&key!="TOFF")return false;
    ApplyCommanderSetting(trimmed);SaveState();PublishMinerSettings();return true;
}
void ApplyCommanderSetting(string trimmed) {
    if (trimmed.StartsWith("SPEEDF ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPositiveDouble(trimmed, 7, ref SPEEDF, "SPEEDF");
        return;
    }

    if (trimmed.StartsWith("SPEEDB ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPositiveDouble(trimmed, 7, ref SPEEDB, "SPEEDB");
        return;
    }

    if (trimmed.StartsWith("MINEH ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPositiveDouble(trimmed, 6, ref MINEH, "MINEH", "m");
        return;
    }

    if (trimmed.StartsWith("MINEW ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPositiveDouble(trimmed, 6, ref MINEW, "MINEW", "m");
        return;
    }

    if (trimmed.StartsWith("AUTOSERVICE ", StringComparison.OrdinalIgnoreCase))
    {
        string mode = trimmed.Substring(12).Trim().ToUpperInvariant();
        if (mode == "ON" || mode == "TRUE" || mode == "1")
            autoServiceReturnEnabled = true;
        else if (mode == "OFF" || mode == "FALSE" || mode == "0")
            autoServiceReturnEnabled = false;
        else
        {
            ScriptLog("Invalid AUTOSERVICE. Use ON or OFF.");
            return;
        }

        ScriptLog("AUTOSERVICE " + (autoServiceReturnEnabled ? "ON" : "OFF"));
        return;
    }

    if (trimmed.StartsWith("AUTOCARGO ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPercentDouble(trimmed, 10, ref autoServiceCargoTriggerPercent, "AUTOCARGO");
        return;
    }

    if (trimmed.StartsWith("AUTOHYDRO ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPercentDouble(trimmed, 10, ref autoServiceHydrogenTriggerPercent, "AUTOHYDRO");
        return;
    }

    if (trimmed.StartsWith("ROUTESTARTEXTRA ", StringComparison.OrdinalIgnoreCase))
    {
        TrySetPositiveDouble(trimmed, 16, ref minerRouteStartExtra, "ROUTESTARTEXTRA", "m");
        minerRouteStartExtra = MathHelper.Clamp(minerRouteStartExtra, 0, 500);
        fullStateDirty = true;
        return;
    }

    if (trimmed.StartsWith("TOFF ", StringComparison.OrdinalIgnoreCase))
    {
        Vector3D off;
        if (TryParseVector3(trimmed.Substring(5).Trim(), out off))
        {
            TOFFR = off.X;
            TOFFU = off.Y;
            TOFFF = off.Z;
            PublishMinerPlannerSettings();
            ScriptLog($"TOFF set. R/U/F = {TOFFR:0.00}/{TOFFU:0.00}/{TOFFF:0.00} m");
        }
        else
            ScriptLog("Invalid TOFF. Use: TOFF r,u,f");
        return;
    }


}
void AddMinerSettings(MyIni ini) {
ini.Set("Settings","SPEEDF",SPEEDF);
ini.Set("Settings","SPEEDB",SPEEDB);
ini.Set("Settings","MINEH",MINEH);
ini.Set("Settings","MINEW",MINEW);
ini.Set("Settings","standardMineDepth",standardMineDepth);
ini.Set("Settings","minerRouteStartExtra",minerRouteStartExtra);
ini.Set("Settings","TOFFR",TOFFR);
ini.Set("Settings","TOFFU",TOFFU);
ini.Set("Settings","TOFFF",TOFFF);
ini.Set("Settings","pointToPointAccelerationLimit",pointToPointAccelerationLimit);
ini.Set("Settings","pointToPointThrustUsePercent",pointToPointThrustUsePercent);
ini.Set("Settings","autoServiceCargoTriggerPercent",autoServiceCargoTriggerPercent);
ini.Set("Settings","autoServiceHydrogenTriggerPercent",autoServiceHydrogenTriggerPercent);
ini.Set("Settings","autoServiceReturnEnabled",autoServiceReturnEnabled);
ini.Set("Settings","mergeAssistedDocking",mergeAssistedDocking);
ini.Set("Settings","emma3ScriptLogging",emma3ScriptLogging);
ini.Set("Settings","timerPersistenceEnabled",timerPersistenceEnabled);
}
void PublishMinerSettings() { MyIni ini=new MyIni();AddMinerSettings(ini);SendModule("settings",ini.ToString()); }
