

void PumpPathLoad()

{

    PathLoadJob job = pathLoadJob;

    if (job == null)

        return;

    if (job.Data == null)

    {

        if (!TryClaimMinerHeavyWork()) return;

        job.Data = job.Timer == null ? "" : job.Timer.CustomData ?? "";

        job.Timer = null;

        return;

    }

    int scanned = 0, records = 0;

    while (job.Position < job.Data.Length && scanned < pathLoadCharactersPerTick && records < pathLoadRecordsPerTick)

    {

        char c = job.Data[job.Position++];

        scanned++;

        if (c != '\n' && c != '\r')

            continue;

        string raw = job.Data.Substring(job.LineStart, job.Position - job.LineStart - 1);

        job.LineStart = job.Position;

        AddLoadedPathRecord(job, raw);

        records++;

    }

    if (job.Position < job.Data.Length)

        return;

    if (job.LineStart < job.Data.Length)

    {

        if (records >= pathLoadRecordsPerTick)

            return;

        AddLoadedPathRecord(job, job.Data.Substring(job.LineStart));

        job.LineStart = job.Data.Length;

        // Publish on a separate tick, never alongside the last record parsing.

        return;

    }

    if (scanned > 0)

        return;

    pathLoadJob = null;

    if (job.Records.Count == 0 && !job.AnchorValid)

    {

        modulePathLoadPending=false;
        moduleOperation.Finish(moduleOperation.Id,"error:empty route");
        SaveState();PublishModuleStatus();
        ScriptLog("Commander route is empty.");

        return;

    }

    CommitLoadedPath(job);

}

