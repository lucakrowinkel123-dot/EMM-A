// Overrides for Miner Pilot. Accept only the commander's versioned C1 payload.
// Keep existing bounded PumpPathLoad/AddLoadedPathRecord/CommitLoadedPath and runners.
bool TryParseCarrierAnchorLine(string line,out Vector3D pos,out Vector3D forward,out Vector3D up)
{pos=forward=up=Vector3D.Zero;string[] p=line.Split('|');return p.Length==4&&p[0]=="A"&&TryParseV3(p[1],out pos)&&TryParseV3(p[2],out forward)&&TryParseV3(p[3],out up);}
bool TryParsePathPointLine(string line,Vector3D anchor,out Vector3D pos,out Vector3D forward,out Vector3D up)
{int flags;return DecodeCanonicalNavigation(line,out pos,out forward,out up,out flags);}
bool DecodeCanonicalNavigation(string line,out Vector3D pos,out Vector3D forward,out Vector3D up,out int flags)
{pos=forward=up=Vector3D.Zero;flags=0;string[] p=line.Split('|');return p.Length==5&&p[0]=="N"&&TryParseV3(p[1],out pos)&&TryParseV3(p[2],out forward)&&TryParseV3(p[3],out up)&&int.TryParse(p[4],out flags)&&flags>=0&&flags<=3;}
bool TryParseGpsCommand(string command,out Vector3D pos,out Vector3D forward,out Vector3D up)
{
    int flags;if(!DecodeCanonicalNavigation(command,out pos,out forward,out up,out flags))return false;
    Vector3D delta=pos-_tickRcPosition;
    if((flags&1)!=0&&delta.LengthSquared()>0.001)forward=Vector3D.Normalize(delta);
    if((flags&2)!=0){Vector3D gravity=flightCache.Gravity;up=gravity.LengthSquared()>0.001?Vector3D.Normalize(-gravity):_tickRcMatrix.Up;}
    return true;
}
bool LineLooksLikeMineArea(string line){return line!=null&&line.StartsWith("M|",StringComparison.Ordinal);}
bool TryParseMineAreaLine(string line,Vector3D anchor,out Vector3D a,out Vector3D b,out Vector3D up,out Vector3D forward,out double depth)
{
    a=b=up=forward=Vector3D.Zero;depth=0;string[] p=line.Split('|');int mode;
    if(p.Length!=7||p[0]!="M"||!TryParseV3(p[1],out a)||!TryParseV3(p[2],out b)||!TryParseV3(p[3],out up)||!TryParseV3(p[4],out forward)||!TryParseNumber(p[5],out depth)||!int.TryParse(p[6],out mode)||mode<0||mode>1)return false;
    if(mode==1){Vector3D gravity=flightCache.Gravity;up=gravity.LengthSquared()>0.001?Vector3D.Normalize(-gravity):_tickRcMatrix.Up;Vector3D toward=b-a;toward-=Vector3D.Dot(toward,up)*up;forward=toward.LengthSquared()>0.001?Vector3D.Normalize(toward):_tickRcMatrix.Forward;}
    return true;
}
bool TryStartAreaMiningCommandFromLane(string command,int startLane)
{
    Vector3D a,b,u,f;double depth;
    if(!command.StartsWith("minearea;",StringComparison.OrdinalIgnoreCase)||!TryParseMineAreaLine(command.Substring(9),pathAnchorPos,out a,out b,out u,out f,out depth)||depth<=0)return false;
    StartAreaMiningWithCornersFromLane(a,b,u,f,depth,startLane);
    return miningEnabled||autopilotEnabled||areaPlanActivationStage!=0;
}
string FormatPathPointLine(Vector3D pos,Vector3D anchor,Vector3D forward,Vector3D up)
{return "N|"+CanonicalVector(pos)+"|"+CanonicalVector(forward)+"|"+CanonicalVector(up)+"|0";}
string CanonicalVector(Vector3D v){return NumberR(v.X)+":"+NumberR(v.Y)+":"+NumberR(v.Z);}
bool TryParseMineAreaSkipMetaLine(string line,out double h,out double w,out int count,List<int> skips,List<double> depths,List<List<DepthRange>> ranges)
{
    h=w=0;count=0;skips.Clear();depths.Clear();ranges.Clear();string[] p=line.Split('|');
    if(p.Length!=7||p[0]!="00;C"||!TryParseNumber(p[1],out h)||!TryParseNumber(p[2],out w)||!int.TryParse(p[3],out count)||h<=0||w<=0||count<=0)return false;
    int n;double value;
    if(p[4].Length>0)foreach(string s in p[4].Split(',')){if(!int.TryParse(s,out n)||n<0)return false;skips.Add(n);}
    if(p[5].Length>0)foreach(string s in p[5].Split(',')){if(!TryParseNumber(s,out value)||value<0)return false;depths.Add(value);}
    if(depths.Count!=0&&depths.Count!=count)return false;
    string[] encoded=p[6].Split(';');if(!int.TryParse(encoded[0],out n)||(n!=0&&n!=count))return false;
    for(int i=0;i<n;i++)ranges.Add(new List<DepthRange>());
    for(int i=1;i<encoded.Length;i++){string[] r=encoded[i].Split(',');int lane;double start,end;if(r.Length!=3||!int.TryParse(r[0],out lane)||lane<0||lane>=ranges.Count||!TryParseNumber(r[1],out start)||!TryParseNumber(r[2],out end)||start<0||end<=start)return false;ranges[lane].Add(new DepthRange(start,end));}
    return true;
}
