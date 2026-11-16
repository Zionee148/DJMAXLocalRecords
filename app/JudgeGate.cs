namespace LadderRecords;
// Missing observations break continuity; absence is a normal candidate only after screen validation.
class JudgeGate
{
 string? candidate;double since,last;
 public string? Confirmed{get;private set;}
 public void Reset(){candidate=Confirmed=null;since=last=0;}
 public string? Observe(string? value,double now){
  if(Confirmed!=null)return Confirmed;
  if(value==null){candidate=null;return null;}
  if(value!=candidate||now-last>2.5){candidate=value;since=now;}
  last=now;if(now-since>=1)Confirmed=value;return Confirmed;
 }
}
