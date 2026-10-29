using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using CrackSaveEditor.Core;

namespace CrackSaveEditor.ViewModels;

public sealed class PipeVm(JsonObject node) : NodeVm(node, Loc.T("World"))
{
    public string Name => Loc.F("Pipe {0}", Node.Str("pipeId"));
    public bool EverBurst => Node.Bool("everBurst");

    public bool Leaking
    {
        get => Node.Bool("leaking");
        set => SetBool("leaking", value, Loc.F("{0} leaking", Name));
    }
}

public sealed class WorldVm : NodeVm
{
    readonly JsonObject _agitation;
    readonly JsonObject _tasks;
    readonly JsonObject _pipes;
    readonly JsonObject _playtest;

    public WorldVm(SaveSlot slot) : base(slot.World, Loc.T("World"))
    {
        _agitation = Node.Obj("agitation");
        _tasks = Node.Obj("dailyTasks");
        _pipes = Node.Obj("burstPipes");
        _playtest = Node.Obj("playtest");
        Pipes = new(_pipes.Arr("pipes").Objects().Select(p => new PipeVm(p)).OrderBy(p => p.Name));

        CalmCommand = new RelayCommand(() => Agitation = 0);
        RepairPipesCommand = new RelayCommand(() => { foreach (var p in Pipes) p.Leaking = false; FloodDepth = 0; });
        CompleteStepCommand = new RelayCommand(() => StepProgress = StepTarget);
        CopySeedCommand = new RelayCommand(() => Clipboard.SetText(WorldSeed.ToString()));
    }

    public ObservableCollection<PipeVm> Pipes { get; }
    public ICommand CalmCommand { get; }
    public ICommand RepairPipesCommand { get; }
    public ICommand CompleteStepCommand { get; }
    public ICommand CopySeedCommand { get; }

    /// <summary>Day as shown in the game (the save stores it zero-based).</summary>
    public int Day
    {
        get => Node.Int("dayNumber") + 1;
        set => SetInt("dayNumber", Math.Clamp(value, 1, 9999) - 1, Loc.T("Day"));
    }

    public long WorldSeed => Node.Long("worldSeed");
    public string RunId => Node.Str("runId");

    public double PlayHours
    {
        get => _playtest.Num("playSeconds") / 3600.0;
        set
        {
            double old = _playtest.Num("playSeconds");
            double now = Math.Max(0, value * 3600.0);
            if (Math.Abs(old - now) < 0.5) return;
            _playtest["playSeconds"] = now;
            S.Track(Area, Loc.T("Playtime"), Fmt.Duration(old), Fmt.Duration(now));
            Raise();
            Raise(nameof(PlayTimeText));
        }
    }

    public string PlayTimeText => Fmt.Duration(_playtest.Num("playSeconds"));

    public double MaxAgitation => GameData.MaxAgitation;

    public double Agitation
    {
        get => _agitation.Num("agitation");
        set
        {
            double old = Agitation;
            double now = Math.Round(Math.Clamp(value, 0, MaxAgitation), 1);
            if (Math.Abs(old - now) < 0.05) return;
            _agitation["agitation"] = now;
            S.Track(Area, Loc.T("Egg agitation"), old, now);
            Raise();
            Raise(nameof(AgitationText));
        }
    }

    public string AgitationText => Agitation switch
    {
        < 25 => Loc.T("Calm"),
        < 50 => Loc.T("Restless"),
        < 75 => Loc.T("Irritated"),
        _ => Loc.T("About to erupt"),
    };

    public int CalmStepsUsed
    {
        get => _agitation.Int("calmStepsUsed");
        set
        {
            int old = CalmStepsUsed;
            if (old == value) return;
            _agitation["calmStepsUsed"] = Math.Max(0, value);
            S.Track(Area, Loc.T("Calming steps used"), old, value);
            Raise();
        }
    }

    public double FloodDepth
    {
        get => _pipes.Num("floodDepth");
        set
        {
            double old = FloodDepth;
            double now = Math.Max(0, value);
            if (Math.Abs(old - now) < 1e-6) return;
            _pipes["floodDepth"] = now;
            S.Track(Area, Loc.T("Flooding"), old, now);
            Raise();
        }
    }

    // ----- Work order -----
    public string StepText => GameData.WorkOrderText(_tasks.Str("stepKey"), StepTarget);
    public string CompletedText => GameData.WorkOrderText(_tasks.Str("completedKey"), null);
    public int StepIndex => _tasks.Int("stepIndex");
    public string StepTitle => Loc.F("Work order – step {0}", StepIndex);
    public int StepTarget => _tasks.Int("stepTarget");

    public int StepProgress
    {
        get => _tasks.Int("stepProgress");
        set
        {
            int old = StepProgress;
            int now = Math.Clamp(value, 0, Math.Max(StepTarget, 0));
            if (old == now) return;
            _tasks["stepProgress"] = now;
            S.Track(Loc.T("Work order"), Loc.T("Progress"), old, now);
            Raise();
            Raise(nameof(StepPercent));
        }
    }

    public double StepPercent => StepTarget > 0 ? StepProgress * 100.0 / StepTarget : 0;
}
