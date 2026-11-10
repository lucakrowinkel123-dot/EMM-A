// Shared by Commander and Pilot. No game API: tested independently of transport.
class ModuleOperationJournal
{
    public string Id = "", Verb = "", Payload = "", State = "", Result = "";
    // The owner persists Serialize() alongside motion state before publishing an
    // acknowledgement. A retry returns that state; it never executes the action twice.
    public bool Accept(string id, string verb, string payload, bool interrupt)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(verb) || id == Id)
            return false;
        if (!IsNewer(id, Id)) return false;
        if (!interrupt && State != "" && State != "acknowledged")
            return false;
        Id = id; Verb = verb; Payload = payload ?? "";
        State = "accepted"; Result = "";
        return true;
    }

    public bool Finish(string id, string result)
    {
        if (id != Id || State != "accepted") return false;
        State = "finished"; Result = result ?? "";
        return true;
    }

    public bool Acknowledge(string id)
    {
        if (id != Id || State != "finished") return false;
        State = "acknowledged";
        return true;
    }

    public string Serialize()
    {
        return "EMMA-OP-1\n" + Part(Id) + Part(Verb) + Part(Payload) + Part(State) + Part(Result);
    }

    static string Part(string value) { return value.Length.ToString() + ":" + value; }

    public static bool IsNewer(string candidate, string previous)
    {
        int split = candidate == null ? -1 : candidate.LastIndexOf('/');
        long sequence;
        if (split <= 0 || !long.TryParse(candidate.Substring(split + 1), out sequence) || sequence <= 0) return false;
        if (string.IsNullOrEmpty(previous)) return true;
        int oldSplit = previous.LastIndexOf('/');
        long oldSequence;
        return oldSplit == split && candidate.Substring(0, split) == previous.Substring(0, oldSplit)
            && long.TryParse(previous.Substring(oldSplit + 1), out oldSequence) && sequence > oldSequence;
    }

    public bool Restore(string value)
    {
        const string header = "EMMA-OP-1\n";
        if (value == null || !value.StartsWith(header, StringComparison.Ordinal)) return false;
        int offset = header.Length;
        string id, verb, payload, state, result;
        if (!Read(value, ref offset, out id) || !Read(value, ref offset, out verb)
            || !Read(value, ref offset, out payload) || !Read(value, ref offset, out state)
            || !Read(value, ref offset, out result) || offset != value.Length) return false;
        if (state != "" && state != "accepted" && state != "finished" && state != "acknowledged") return false;
        if (state != "" && (!IsNewer(id, "") || verb.Length == 0)) return false;
        if (state == "" && (id.Length != 0 || verb.Length != 0 || payload.Length != 0 || result.Length != 0)) return false;
        Id = id; Verb = verb; Payload = payload; State = state; Result = result;
        return true;
    }

    static bool Read(string value, ref int offset, out string part)
    {
        part = "";
        int end = value.IndexOf(':', offset), length;
        if (end < offset || end - offset > 9 || !int.TryParse(value.Substring(offset, end - offset), out length)
            || length < 0 || length > value.Length - end - 1) return false;
        part = value.Substring(end + 1, length);
        offset = end + 1 + length;
        return true;
    }
}
