void ApplyMinerSettings(MyIni ini) {
 bool previousMerge=mergeAssistedDocking;
SPEEDF=ini.Get("Settings","SPEEDF").ToDouble(SPEEDF);
SPEEDB=ini.Get("Settings","SPEEDB").ToDouble(SPEEDB);
MINEH=ini.Get("Settings","MINEH").ToDouble(MINEH);
MINEW=ini.Get("Settings","MINEW").ToDouble(MINEW);
standardMineDepth=ini.Get("Settings","standardMineDepth").ToDouble(standardMineDepth);
minerRouteStartExtra=ini.Get("Settings","minerRouteStartExtra").ToDouble(minerRouteStartExtra);
TOFFR=ini.Get("Settings","TOFFR").ToDouble(TOFFR);
TOFFU=ini.Get("Settings","TOFFU").ToDouble(TOFFU);
TOFFF=ini.Get("Settings","TOFFF").ToDouble(TOFFF);
pointToPointAccelerationLimit=ini.Get("Settings","pointToPointAccelerationLimit").ToDouble(pointToPointAccelerationLimit);
pointToPointThrustUsePercent=ini.Get("Settings","pointToPointThrustUsePercent").ToDouble(pointToPointThrustUsePercent);
autoServiceCargoTriggerPercent=ini.Get("Settings","autoServiceCargoTriggerPercent").ToDouble(autoServiceCargoTriggerPercent);
autoServiceHydrogenTriggerPercent=ini.Get("Settings","autoServiceHydrogenTriggerPercent").ToDouble(autoServiceHydrogenTriggerPercent);
autoServiceReturnEnabled=ini.Get("Settings","autoServiceReturnEnabled").ToBoolean(autoServiceReturnEnabled);
mergeAssistedDocking=ini.Get("Settings","mergeAssistedDocking").ToBoolean(mergeAssistedDocking);
emma3ScriptLogging=ini.Get("Settings","emma3ScriptLogging").ToBoolean(emma3ScriptLogging);
SetTimerPersistence(ini.Get("Settings","timerPersistenceEnabled").ToBoolean(timerPersistenceEnabled));
 if(previousMerge&&!mergeAssistedDocking)SetMergerEnabled(false);
}
// The Commander is the sole reader/writer of behavior settings.
void LoadCarrierMinerSettings() { }
void PublishMinerPlannerSettings() { }
