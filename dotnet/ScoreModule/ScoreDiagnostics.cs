// SCORE 진단 기록. 텍스트 기록만 맡는다. 진단 이벤트를 한 줄에 하나씩 JSON 으로 기록한다.
//
//   - 개인 점수 기록과 다른 폴더(실행 파일 옆 diagnostics)에 따로 둔다. 점수 기록을 읽거나 바꾸지 않는다. 어디로도 보내지 않는다.
//   - 파일 하나가 FileLimit 를 넘으면 다음 번호의 파일로 넘어가고, 합계가 TotalLimit 를 넘으면 오래된 파일부터 지운다.
//   - 지우는 것은 이 클래스가 만든 파일(score-번호.log)뿐이다.
//   - 기록에 실패하면 오류 메시지를 LastError 에 저장하고 앱 실행을 계속한다(진단 기록 때문에 앱이 멈추면 안 된다). 다음 기록이 성공하면 LastError 를 지운다.
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DjmaxScore;

public sealed class ScoreDiagnostics
{
    public const long DefaultFileLimit = 2L * 1024 * 1024, DefaultTotalLimit = 10L * 1024 * 1024;
    private static readonly Regex Ours = new(@"^score-(\d{4})\.log$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly string _folder;
    private readonly Func<DateTimeOffset> _clock;
    private readonly long _fileLimit, _totalLimit;
    private int _index = -1;

    public string Folder => _folder;
    public string? LastError { get; private set; }

    public ScoreDiagnostics(string folder, Func<DateTimeOffset>? clock = null, long fileLimit = DefaultFileLimit, long totalLimit = DefaultTotalLimit)
    {
        _folder = folder; _clock = clock ?? (() => DateTimeOffset.Now); _fileLimit = fileLimit; _totalLimit = totalLimit;
    }

    private string PathOf(int index) => Path.Combine(_folder, $"score-{index:D4}.log");

    private List<(int Index, FileInfo File)> Existing() =>
        new DirectoryInfo(_folder).EnumerateFiles("score-*.log").Select(f => (Match: Ours.Match(f.Name), File: f)).Where(x => x.Match.Success)
            .Select(x => (int.Parse(x.Match.Groups[1].Value, CultureInfo.InvariantCulture), x.File)).OrderBy(x => x.Item1).ToList();

    /// <summary>진단 이벤트 하나를 적는다. kind 는 이벤트 이름, fields 는 이름 붙은 값들(익명 형식).</summary>
    public void Write(string kind, object fields)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            var line = new StringBuilder();
            line.Append("{\"at\":\"").Append(_clock().ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("\",\"kind\":\"").Append(kind).Append('"');
            string body = JsonSerializer.Serialize(fields, Json);
            if (body.Length > 2) line.Append(',').Append(body, 1, body.Length - 2);
            line.Append("}\n");
            byte[] bytes = Encoding.UTF8.GetBytes(line.ToString());

            var files = Existing();
            if (_index < 0) _index = files.Count > 0 ? files[^1].Index : 1;
            long size = files.FirstOrDefault(f => f.Index == _index).File?.Length ?? 0;
            if (size > 0 && size + bytes.Length > _fileLimit) { _index++; size = 0; }
            using (var stream = new FileStream(PathOf(_index), FileMode.Append, FileAccess.Write, FileShare.Read)) stream.Write(bytes);

            // 합계를 넘으면 오래된 것부터 지운다(지금 쓰는 파일은 남긴다)
            files = Existing();
            long total = files.Sum(f => f.File.Length);
            foreach (var (index, file) in files)
            {
                if (total <= _totalLimit || index == _index) break;
                total -= file.Length;
                file.Delete();
            }
            LastError = null;
        }
        catch (Exception e) { LastError = e.Message; }
    }
}
