using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

namespace DemonsSouls.Launcher;

public sealed class OperationTrace : IDisposable
{
    static readonly AsyncLocal<OperationTrace?> active = new();
    readonly string operation;
    readonly Stopwatch elapsed=Stopwatch.StartNew();
    readonly string operationId = Guid.NewGuid().ToString("N");
    readonly string logPath;
    string? currentPhase;
    string? lastPhase;
    Phase? currentPhaseMetrics;
    bool finished;
    readonly ConcurrentDictionary<(string Phase,string Activity),ActivityTotals> activities=new();
    long auxiliaryBytesRead;
    long auxiliaryBytesHashed;
    public long AuxiliaryBytesRead=>Interlocked.Read(ref auxiliaryBytesRead);
    public long AuxiliaryBytesHashed=>Interlocked.Read(ref auxiliaryBytesHashed);
    public static bool IsActive=>active.Value is not null;
    long totalRead,totalWritten,totalHashed,totalReused,totalHashesReused;
    public long HashesReused=>Interlocked.Read(ref totalHashesReused);
    public long ContentBytesRead=>Interlocked.Read(ref totalRead);
    public long ContentBytesHashed=>Interlocked.Read(ref totalHashed);
    public long ContentBytesWritten=>Interlocked.Read(ref totalWritten);
    public long ContentBytesReused=>Interlocked.Read(ref totalReused);
    public string LogPath => logPath;

    OperationTrace(string operation)
    {
        this.operation = operation;
        string directory = Path.Combine(Preferences.UserDataDirectory, "logs", "operations");
        Directory.CreateDirectory(directory);
        logPath = Path.Combine(directory, operationId + ".jsonl");
        Write("operation_started", operation, null, 0, 0, null);
    }

    public static OperationTrace Start(string operation) => new(operation);

    public IDisposable Activate()
    {
        var previous = active.Value;
        active.Value = this;
        return new Activation(previous);
    }

    public static Phase? Measure(string name, int files = 0, long bytes = 0) =>
        active.Value?.BeginPhase(name, files, bytes);
    // Hot operations aggregate in memory. No per-file log writes.
    public static IDisposable? MeasureActivity(string name) => active.Value is {} trace
        ?new ActivityScope(trace,trace.currentPhase??"outside_named_phase",name):null;
    public static void Observe(string name)
    {
        if(active.Value is {} trace)
            Interlocked.Increment(ref trace.activities.GetOrAdd((trace.currentPhase??"outside_named_phase",name),_=>new()).Calls);
    }
    public static byte[] ReadAuxiliaryBytes(string path)
    {
        byte[] data=File.ReadAllBytes(path);RecordAuxiliaryRead(data.Length);return data;
    }
    public static string HashAuxiliaryFile(string path)
    {
        byte[] data=ReadAuxiliaryBytes(path);
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
        if(active.Value is {} trace)Interlocked.Add(ref trace.auxiliaryBytesHashed,data.Length);
        return hash;
    }
    public static string ReadAuxiliaryText(string path)
    {
        using var stream=File.OpenRead(path);
        using var reader=new StreamReader(stream,Encoding.UTF8,detectEncodingFromByteOrderMarks:true);
        try{return reader.ReadToEnd();}
        finally{RecordAuxiliaryRead(stream.Position);}
    }
    public static void RecordAuxiliaryRead(long bytes)
    {if(active.Value is {} trace)Interlocked.Add(ref trace.auxiliaryBytesRead,bytes);}
    void FlushActivities()
    {
        foreach(var item in activities.OrderBy(item=>item.Key.Phase).ThenBy(item=>item.Key.Activity))
            File.AppendAllText(logPath,JsonSerializer.Serialize(new
            {
                eventName="activity_summary",operationId,operation,phase=item.Key.Phase,activity=item.Key.Activity,
                calls=item.Value.Calls,durationMilliseconds=1000.0*item.Value.Ticks/Stopwatch.Frequency,
                bytesRead=item.Value.Read,bytesHashed=item.Value.Hashed,auxiliaryBytesRead=item.Value.Auxiliary,
                auxiliaryBytesHashed=item.Value.AuxiliaryHashed,
                durationKind="inclusive; nested activities overlap",physicalDiskBytes=(long?)null
            })+Environment.NewLine,new UTF8Encoding(false));
    }
    public static void RecordIo(long bytesRead,long bytesWritten)
    {
        if(active.Value is not {} trace)return;
        Interlocked.Add(ref trace.totalRead,bytesRead);Interlocked.Add(ref trace.totalWritten,bytesWritten);
        trace.currentPhaseMetrics?.AddIo(bytesRead,bytesWritten);
    }
    public static void RecordHash(long bytes)
    {
        if(active.Value is not {} trace)return;
        Interlocked.Add(ref trace.totalHashed,bytes);trace.currentPhaseMetrics?.AddHash(bytes);
    }
    public static void RecordReuse(long bytes)
    {if(active.Value is {} trace){Interlocked.Add(ref trace.totalReused,bytes);Interlocked.Increment(ref trace.totalHashesReused);}}
    public static void RecordDecision(string decision,string detail)
    {
        var trace=active.Value;
        trace?.Write("decision",decision,trace.currentPhase,0,0,null,decision:detail);
    }

    public void Complete()
    {
        if (finished) return;
        FlushActivities();
        Write("operation_finished", "completed", currentPhase ?? lastPhase, 0, 0, null,elapsed.ElapsedMilliseconds);
        finished = true;
    }

    public void Fail(Exception error)
    {
        if (finished) return;
        FlushActivities();
        Write(error is OperationCanceledException ? "operation_cancelled" : "operation_failed",
            null, currentPhase ?? lastPhase, 0, 0, error.ToString(),elapsed.ElapsedMilliseconds);
        finished = true;
    }

    Phase BeginPhase(string name, int files, long bytes)
    {
        string? previous = currentPhase;
        Phase? previousMetrics=currentPhaseMetrics;
        currentPhase = lastPhase = name;
        var phase=new Phase(this, name, previous,previousMetrics, files, bytes);
        currentPhaseMetrics=phase;
        Write("phase_started", name, name, files, bytes, null);
        return phase;
    }

    void EndPhase(string name, string? previous,Phase? previousMetrics,int files, long bytes,
        long bytesRead,long bytesWritten,long elapsedMilliseconds, bool completed)
    {
        Write("phase_finished", completed ? "completed" : "interrupted", name, files, bytes, null,
            elapsedMilliseconds,bytesRead,bytesWritten);
        currentPhase = previous;
        currentPhaseMetrics=previousMetrics;
    }

    void Write(string eventName, string? status, string? phase, int files, long bytes, string? error,
        long durationMilliseconds = 0,long bytesRead=0,long bytesWritten=0,string? decision=null)
    {
        var record = new
        {
            at = DateTimeOffset.Now,
            operationId,
            operation,
            eventName,
            status,
            phase,
            files,
            bytes,
            bytesRead,
            bytesWritten,
            contentBytesRead=ContentBytesRead,
            contentBytesHashed=ContentBytesHashed,
            contentBytesWritten=ContentBytesWritten,
            contentBytesReused=ContentBytesReused,
            hashesReused=HashesReused,
            auxiliaryBytesRead=AuxiliaryBytesRead,
            auxiliaryBytesHashed=AuxiliaryBytesHashed,
            physicalDiskBytes=(long?)null,
            bytesHashed = currentPhaseMetrics?.BytesHashed ?? 0,
            durationMilliseconds,
            decision,
            error
        };
        File.AppendAllText(logPath, JsonSerializer.Serialize(record) + Environment.NewLine, new UTF8Encoding(false));
    }

    public void Dispose()
    {
        if (!finished) Fail(new IOException("A operação foi encerrada antes de concluir."));
    }

    sealed class Activation(OperationTrace? previous) : IDisposable
    {
        public void Dispose() => active.Value = previous;
    }
    sealed class ActivityTotals
    {
        public long Calls,Ticks,Read,Hashed,Auxiliary,AuxiliaryHashed;
    }
    sealed class ActivityScope : IDisposable
    {
        readonly OperationTrace trace;
        readonly ActivityTotals totals;
        readonly long start=Stopwatch.GetTimestamp(),read,hashed,auxiliary,auxiliaryHashed;
        public ActivityScope(OperationTrace trace,string phase,string name)
        {
            this.trace=trace;totals=trace.activities.GetOrAdd((phase,name),_=>new());
            read=trace.ContentBytesRead;hashed=trace.ContentBytesHashed;auxiliary=trace.AuxiliaryBytesRead;
            auxiliaryHashed=trace.AuxiliaryBytesHashed;
        }
        public void Dispose()
        {
            Interlocked.Increment(ref totals.Calls);
            Interlocked.Add(ref totals.Ticks,Stopwatch.GetTimestamp()-start);
            Interlocked.Add(ref totals.Read,trace.ContentBytesRead-read);
            Interlocked.Add(ref totals.Hashed,trace.ContentBytesHashed-hashed);
            Interlocked.Add(ref totals.Auxiliary,trace.AuxiliaryBytesRead-auxiliary);
            Interlocked.Add(ref totals.AuxiliaryHashed,trace.AuxiliaryBytesHashed-auxiliaryHashed);
        }
    }

    public sealed class Phase : IDisposable
    {
        readonly OperationTrace owner;
        readonly string name;
        readonly string? previous;
        readonly Phase? previousMetrics;
        readonly Stopwatch timer = Stopwatch.StartNew();
        int files;
        long bytes;
        long bytesRead;
        long bytesWritten;
        long bytesHashed;
        bool completed;

        internal Phase(OperationTrace owner, string name, string? previous,Phase? previousMetrics,int files, long bytes)
        {
            this.owner = owner;
            this.name = name;
            this.previous = previous;
            this.previousMetrics=previousMetrics;
            this.files = files;
            this.bytes = bytes;
        }

        public void SetCounts(int fileCount, long byteCount)
        {
            files = fileCount;
            bytes = byteCount;
        }

        public void Complete() => completed = true;
        internal void AddIo(long read,long written)
        {
            Interlocked.Add(ref bytesRead,read);
            Interlocked.Add(ref bytesWritten,written);
        }
        internal long BytesHashed => Interlocked.Read(ref bytesHashed);
        internal void AddHash(long bytes)=>Interlocked.Add(ref bytesHashed,bytes);

        public void Dispose()
        {
            timer.Stop();
            owner.EndPhase(name, previous,previousMetrics,files,bytes,bytesRead,bytesWritten,
                timer.ElapsedMilliseconds,completed);
        }
    }
}
