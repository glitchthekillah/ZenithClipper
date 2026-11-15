using ZenithClipper.Editing;

namespace ZenithClipper.Editor.Desktop;

/// <summary>Headless checks for the Premiere-style edit commands.</summary>
internal static class PremiereChecks
{
    static (EditProject P, EditSequence S, EditTrack V, EditTrack A, MediaAsset M) Fixture()
    {
        var fps = new FrameRate(30, 1);
        var m = new MediaAsset { Name = "m", Kind = MediaKind.Video, DurationSeconds = 100, Width = 1920, Height = 1080, Fps = fps, HasAudio = true };
        var s = new EditSequence { Fps = fps };
        EditTrack v = new() { Name = "Video 1", Kind = TrackKind.Video }, a = new() { Name = "Audio 1", Kind = TrackKind.Audio };
        s.Tracks = [v, a];
        var p = new EditProject { ActiveSequenceId = s.Id, Sequences = [s], Media = [m] };
        return (p, s, v, a, m);
    }
    static void Expect(bool ok, string what) { if (!ok) throw new InvalidOperationException("Premiere edit check failed: " + what); }

    public static void Run()
    {
        // Sync lock: ripple-deleting on V1 closes the same gap on a sync-locked music track, not on an unlocked-sync one.
        {
            var (yp, ys, yv, ya, ym) = Fixture();
            yv.Clips.Add(new TimelineClip { MediaId = ym.Id, Start = 0, Duration = 30, StreamIntent = ClipStreamIntent.VideoOnly });
            yv.Clips.Add(new TimelineClip { MediaId = ym.Id, Start = 30, Duration = 30, StreamIntent = ClipStreamIntent.VideoOnly });
            ya.Clips.Add(new TimelineClip { MediaId = ym.Id, Start = 40, Duration = 20, StreamIntent = ClipStreamIntent.AudioOnly });
            var free = new EditTrack { Name = "A2", Kind = TrackKind.Audio, SyncLock = false }; ys.Tracks.Add(free);
            free.Clips.Add(new TimelineClip { MediaId = ym.Id, Start = 40, Duration = 20, StreamIntent = ClipStreamIntent.AudioOnly });
            EditCommands.DeleteMany(yp, ys.Id, [yv.Clips[0].Id], ripple: true);
            Expect(yv.Clips[0].Start == 0 && ya.Clips[0].Start == 10 && free.Clips[0].Start == 40, "sync lock shifts locked tracks only");
        }
        // FCP7 XML round trip: export a linked clip pair and a second clip, read it back, rebuild the same cuts.
        {
            var (xp, xs, xv, xa, xm) = Fixture(); xm.Path = Path.Combine(Path.GetTempPath(), "My Clips", "game clip #1.mp4");
            EditCommands.AppendMedia(xp, xs.Id, xm.Id, 0); foreach (var c in xv.Clips.Concat(xa.Clips)) { c.Duration = 60; c.SourceIn = 12; }
            xv.Clips.Add(new TimelineClip { MediaId = xm.Id, Start = 90, Duration = 30, SourceIn = 200, StreamIntent = ClipStreamIntent.VideoOnly });
            var xml = XmlInterchange.Write(xp, xs); var back = XmlInterchange.Read(xml);
            var seq = XmlInterchange.Build(back, new Dictionary<string, MediaAsset> { [back.Clips[0].FileId] = xm });
            var xmlV = seq.Tracks.First(t => t.Kind == TrackKind.Video).Clips; var xmlA = seq.Tracks.First(t => t.Kind == TrackKind.Audio).Clips;
            Expect(back.Clips[0].Path.EndsWith(Path.GetFileName(xm.Path), StringComparison.Ordinal) && xmlV.Count == 2 && xmlA.Count == 1 && xmlV[0].SourceIn == 12 && xmlV[0].Duration == 60 && xmlV[1].Start == 90 && xmlV[0].LinkId is not null && xmlV[0].LinkId == xmlA[0].LinkId && xmlV[1].LinkId is null, "FCP XML round trip");
        }
        Expect(EditorWindow.SequencePattern(Path.Combine("d", "shot_0042.png")) is { Start: 42 } seqPattern && seqPattern.Pattern.EndsWith("shot_%04d.png", StringComparison.Ordinal) && EditorWindow.SequencePattern("title.png") is null, "image sequence pattern detection");
        // Multicam: two cameras stack in a nested sequence; switching cuts and changes the angle.
        {
            var (mp, ms, mv, ma, mm) = Fixture();
            var c1 = EditCommands.AppendMedia(mp, ms.Id, mm.Id, 0); foreach (var c in mv.Clips.Concat(ma.Clips)) c.Duration = 90;
            var v2 = new EditTrack { Name = "Video 2", Kind = TrackKind.Video }; ms.Tracks.Insert(1, v2);
            var cam2 = new TimelineClip { MediaId = mm.Id, Start = 0, SourceIn = 300, Duration = 80, StreamIntent = ClipStreamIntent.VideoOnly }; v2.Clips.Add(cam2);
            var multi = EditCommands.CreateMulticam(mp, ms.Id, [c1.VideoClipId!.Value, cam2.Id], "Multicam");
            var inner = mp.Sequences.Last();
            Expect(inner.Tracks.Count(t => t.Kind == TrackKind.Video) == 2 && mv.Clips.Single().Id == multi && mv.Clips[0].Duration == 90 && ma.Clips.Count == 0 && v2.Clips.Count == 0, "multicam stacks cameras and replaces them");
            EditCommands.SwitchAngle(mp, ms.Id, mv.Id, multi, 40, 2);
            Expect(mv.Clips.Count == 2 && mv.Clips[0].MulticamAngle == 1 && mv.Clips[1].MulticamAngle == 2 && mv.Clips[1].Start == 40, "switching cuts to camera 2");
            bool bad = false; try { EditCommands.SwitchAngle(mp, ms.Id, mv.Id, mv.Clips[1].Id, 60, 5); } catch (InvalidDataException) { bad = true; }
            Expect(bad, "a missing camera is rejected");
        }
        // Speed/Duration with ripple: slowing a linked clip to 50% doubles it and pushes trailing clips on both tracks.
        {
            var (rp, rs, rv, ra, rm) = Fixture();
            var lead = EditCommands.AppendMedia(rp, rs.Id, rm.Id, 0); foreach (var c in rv.Clips.Concat(ra.Clips)) c.Duration = 60;
            EditCommands.AppendMedia(rp, rs.Id, rm.Id, 60); foreach (var c in rv.Clips.Concat(ra.Clips).Where(c => c.Start == 60)) c.Duration = 60;
            EditCommands.SetSpeedRipple(rp, rs.Id, lead.VideoClipId!.Value, .5, ripple: true);
            Expect(rv.Clips[0].Duration == 120 && ra.Clips[0].Duration == 120 && rv.Clips[1].Start == 120 && ra.Clips[1].Start == 120, "speed ripple pushes trailing clips");
            EditCommands.SetSpeedRipple(rp, rs.Id, lead.VideoClipId!.Value, 2, ripple: true);
            Expect(rv.Clips[0].Duration == 30 && rv.Clips[1].Start == 30, "speed ripple pulls trailing clips back");
        }
        // Subclips: appending one uses only its range, video and audio alike; a bad range is rejected.
        {
            var (sp, ss, sv, sa, sm) = Fixture();
            var sub = ProjectValidation.Clone(sm); sub.Id = Guid.NewGuid(); sub.SubclipIn = 1; sub.SubclipOut = 3; sp.Media.Add(sub);
            var r = EditCommands.AppendMedia(sp, ss.Id, sub.Id, 0);
            Expect(sv.Clips[0].SourceIn == 30 && sv.Clips[0].Duration == 60 && sa.Clips[0].SourceIn == 30 && sa.Clips[0].Duration == 60, "subclip appends its own range");
            ProjectValidation.Validate(sp);
            sub.SubclipOut = 9999; bool bad = false; try { ProjectValidation.Validate(sp); } catch (InvalidDataException) { bad = true; }
            Expect(bad, "subclip range past the file is rejected");
        }
        // Groups: closure follows links and groups; moving one grouped clip moves the rest; multi-delete removes all.
        {
            var (gp, gs, gv, ga, gm) = Fixture();
            var pair = EditCommands.AppendMedia(gp, gs.Id, gm.Id, 0); foreach (var c in gv.Clips.Concat(ga.Clips)) c.Duration = 60;
            var other = new TimelineClip { MediaId = gm.Id, Start = 100, Duration = 30, StreamIntent = ClipStreamIntent.VideoOnly }; gv.Clips.Add(other);
            EditCommands.Group(gp, gs.Id, [pair.VideoClipId!.Value, other.Id]);
            Expect(EditCommands.Closure(gp, gs.Id, [other.Id]).Count == 3, "group closure includes the linked audio");
            EditCommands.MoveOverwrite(gp, gs.Id, other.Id, 130);
            Expect(gv.Clips.Single(c => c.Id == other.Id).Start == 130 && gv.Clips.Single(c => c.Id == pair.VideoClipId).Start == 30 && ga.Clips[0].Start == 30, "grouped clips move together");
            var copied = EditCommands.Paste(gp, gs.Id, [(gv.Id, ProjectValidation.Clone(other))], 400, false);
            Expect(gv.Clips.Single(c => c.Id == copied[0]).GroupId != other.GroupId, "pasted copies get their own group");
            EditCommands.DeleteMany(gp, gs.Id, [other.Id], ripple: false);
            Expect(gv.Clips.Count == 1 && ga.Clips.Count == 0, "deleting a grouped clip deletes its group");
        }
        // Auto-ducking: the music dips 12 dB around dialogue (frames 100-150) with 0.4 s fades and recovers after.
        {
            var (dp, ds, dv, da, dm) = Fixture();
            var music = new EditTrack { Name = "Music", Kind = TrackKind.Audio }; ds.Tracks.Add(music);
            var song = new TimelineClip { MediaId = dm.Id, Duration = 300, GainDb = -3, StreamIntent = ClipStreamIntent.AudioOnly }; music.Clips.Add(song);
            Expect(Ducking.Apply(ds, music.Id, f => (f >= 100 && f < 150) || (f >= 156 && f < 170)) == 1, "ducking changes the music clip");
            var keys = song.Motion.Where(k => k.Parameter == "volume").Select(k => (k.Frame, k.Value)).ToList();
            Expect(keys.SequenceEqual(new (long, double)[] { (0, -3), (88, -3), (100, -15), (170, -15), (182, -3), (300, -3) }), "duck keys: " + string.Join(" ", keys));
            ProjectValidation.Validate(dp);
        }
        // Time remapping: source use follows the speed curve; split keeps the curve and the source position continuous.
        {
            var (rp, rs, rv, ra, rm) = Fixture();
            var c = new TimelineClip { MediaId = rm.Id, Duration = 300, StreamIntent = ClipStreamIntent.VideoOnly }; rv.Clips.Add(c);
            c.Motion.Add(new Keyframe { Parameter = "speed", Frame = 0, Value = 1 }); c.Motion.Add(new Keyframe { Parameter = "speed", Frame = 100, Value = 2 });
            ProjectValidation.Validate(rp);
            double used = TimeRemap.Consumed(c, 300);
            Expect(used > 540 && used < 560, $"ramp uses the right amount of source ({used:0.#})");
            Expect(TimeRemap.Runs(c).Sum(r => r.Length) == 300 && TimeRemap.Runs(c).Select(r => r.Speed).Distinct().Count() <= 20, "remap runs cover the clip with few speeds");
            long left = (long)Math.Round(TimeRemap.Consumed(c, 150));
            EditCommands.Split(rp, rs.Id, rv.Id, c.Id, 150);
            var right = rv.Clips.Single(x => x.Start == 150);
            Expect(right.SourceIn == left && Math.Abs(TimeRemap.SpeedAt(right, 0) - TimeRemap.SpeedAt(rv.Clips[0], 149)) < .1, "split continues the remapped source and speed");
            bool rejected = false; try { EditCommands.RateStretch(rp, rs.Id, rv.Id, right.Id, 50); } catch (InvalidOperationException) { rejected = true; }
            Expect(rejected, "rate stretch refuses remapped clips");
            rv.Clips[0].LinkId = Guid.NewGuid(); bool linked = false; try { ProjectValidation.Validate(rp); } catch (InvalidDataException) { linked = true; }
            Expect(linked, "remapped clips must be unlinked");
        }
        // Nest: a linked clip pair moves into a new sequence; one nested clip takes its place; nesting into itself fails.
        {
            var (np, ns, nv, na, nm) = Fixture();
            var pair = EditCommands.AppendMedia(np, ns.Id, nm.Id, 0); nv.Clips[0].Start = 30; na.Clips[0].Start = 30;
            var nestedId = EditCommands.Nest(np, ns.Id, [pair.VideoClipId!.Value], "Nested");
            Expect(np.Sequences.Count == 2 && nv.Clips.Count == 1 && na.Clips.Count == 0 && nv.Clips[0].Id == nestedId && nv.Clips[0].Start == 30, "nest replaces linked clips");
            var inner = np.Sequences[1]; Expect(inner.Tracks.Sum(t => t.Clips.Count) == 2 && inner.Tracks.SelectMany(t => t.Clips).All(c => c.Start == 0), "nested sequence keeps relative timing");
            var media = np.Media.Single(x => x.Kind == MediaKind.Sequence);
            inner.Tracks[0].Clips.Add(new TimelineClip { MediaId = media.Id, Start = 500, Duration = 10 });
            bool cycle = false; try { ProjectValidation.Validate(np); } catch (InvalidDataException) { cycle = true; }
            Expect(cycle, "a sequence can't contain itself");
            inner.Tracks[0].Clips.RemoveAt(inner.Tracks[0].Clips.Count - 1);
            nv.Clips[0].Speed = 2; bool speed = false; try { ProjectValidation.Validate(np); } catch (InvalidDataException) { speed = true; }
            Expect(speed, "nested clips play at normal speed"); nv.Clips[0].Speed = 1;
            ProjectValidation.Validate(np);
            bool used = false; try { EditCommands.DeleteSequence(np, inner.Id); } catch (InvalidOperationException) { used = true; }
            Expect(used, "nested sequence in use can't be deleted");
        }
        // Add edit to all tracks cuts linked video and audio at once.
        var (p, s, v, a, m) = Fixture();
        EditCommands.AppendMedia(p, s.Id, m.Id, 0); EditCommands.AppendMedia(p, s.Id, m.Id, 300);
        foreach (var c in v.Clips.Concat(a.Clips)) c.Duration = 300;
        Expect(EditCommands.AddEditAll(p, s.Id, 150) == 2 && v.Clips.Count == 3 && a.Clips.Count == 3, "add edit to all tracks");

        // W: ripple trim the tail to the playhead; everything later closes up on both tracks.
        (p, s, v, a, m) = Fixture();
        var first = EditCommands.AppendMedia(p, s.Id, m.Id, 0); var second = EditCommands.AppendMedia(p, s.Id, m.Id, 3000);
        long secondStart = v.Clips.Single(c => c.Id == second.VideoClipId).Start;
        EditCommands.RippleTrimToPlayhead(p, s.Id, v.Id, 100, head: false);
        Expect(v.Clips.Single(c => c.Id == first.VideoClipId).End == 100 && a.Clips.Single(c => c.Id == first.AudioClipId).End == 100, "W trims linked tail");
        Expect(v.Clips.Single(c => c.Id == second.VideoClipId).Start == 3000 - 2900 && a.Clips.Single(c => c.Id == second.AudioClipId).Start == 100, $"W ripples later clips (was {secondStart})");

        // Q: ripple trim the head; the clip's remaining content now starts where the old clip started.
        (p, s, v, a, m) = Fixture();
        first = EditCommands.AppendMedia(p, s.Id, m.Id, 0);
        EditCommands.RippleTrimToPlayhead(p, s.Id, v.Id, 60, head: true);
        var q = v.Clips.Single(); Expect(q.Start == 0 && q.SourceIn == 60 && a.Clips.Single().Start == 0, "Q trims head and ripples");

        // Close a gap.
        (p, s, v, a, m) = Fixture();
        v.Clips.Add(new TimelineClip { MediaId = m.Id, Start = 0, Duration = 30 }); v.Clips.Add(new TimelineClip { MediaId = m.Id, Start = 90, Duration = 30 });
        EditCommands.CloseGap(p, s.Id, v.Id, 50); Expect(v.Clips[1].Start == 30, "ripple delete gap");

        // Extract In/Out removes the range and closes it; Lift leaves the gap.
        (p, s, v, a, m) = Fixture();
        v.Clips.Add(new TimelineClip { MediaId = m.Id, Start = 0, Duration = 300 });
        EditCommands.LiftExtract(p, s.Id, 100, 160, extract: true);
        Expect(v.Clips.Count == 2 && v.Clips[0].End == 100 && v.Clips[1].Start == 100 && v.Clips[1].Duration == 140 && v.Clips[1].SourceIn == 160, "extract");
        (p, s, v, a, m) = Fixture();
        v.Clips.Add(new TimelineClip { MediaId = m.Id, Start = 0, Duration = 300 });
        EditCommands.LiftExtract(p, s.Id, 100, 160, extract: false);
        Expect(v.Clips.Count == 2 && v.Clips[1].Start == 160, "lift");

        // Paste insert pushes later material right; pasted linked pair stays linked with a new link id.
        (p, s, v, a, m) = Fixture();
        var src = EditCommands.AppendMedia(p, s.Id, m.Id, 0); foreach (var c in v.Clips.Concat(a.Clips)) c.Duration = 60;
        var board = new List<(Guid, TimelineClip)> { (v.Id, ProjectValidation.Clone(v.Clips[0])), (a.Id, ProjectValidation.Clone(a.Clips[0])) };
        var made = EditCommands.Paste(p, s.Id, board, 30, insert: true);
        Expect(made.Count == 2 && v.Clips.Count == 3 && v.Clips.Max(c => c.End) == 120, "paste insert");
        var pastedLinks = v.Clips.Concat(a.Clips).Where(c => made.Contains(c.Id)).Select(c => c.LinkId).Distinct().ToList();
        Expect(pastedLinks.Count == 1 && pastedLinks[0] != v.Clips.Single(c => c.Id == src.VideoClipId).LinkId, "pasted clips keep a fresh link");

        // Slide keeps the clip's content and length while its neighbours give and take.
        (p, s, v, a, m) = Fixture();
        v.Clips.AddRange([new TimelineClip { MediaId = m.Id, Start = 0, SourceIn = 300, Duration = 60 }, new TimelineClip { MediaId = m.Id, Start = 60, SourceIn = 600, Duration = 60 }, new TimelineClip { MediaId = m.Id, Start = 120, SourceIn = 900, Duration = 60 }]);
        EditCommands.Slide(p, s.Id, v.Id, v.Clips[1].Id, 10);
        Expect(v.Clips[0].Duration == 70 && v.Clips[1].Start == 70 && v.Clips[1].SourceIn == 600 && v.Clips[2].Start == 130 && v.Clips[2].Duration == 50 && v.Clips[2].SourceIn == 910, "slide");

        // Frame hold freezes from the playhead on.
        (p, s, v, a, m) = Fixture();
        v.Clips.Add(new TimelineClip { MediaId = m.Id, Start = 0, SourceIn = 30, Duration = 90 });
        EditCommands.AddFrameHold(p, s.Id, v.Id, v.Clips[0].Id, 40, false, 0);
        Expect(v.Clips.Count == 2 && v.Clips[1].HoldFrame == 70 && v.Clips[1].Start == 40, "frame hold");

        // Transitions validate and split hands them to the right halves.
        (p, s, v, a, m) = Fixture();
        v.Clips.Add(new TimelineClip { MediaId = m.Id, Start = 0, Duration = 90 });
        EditCommands.SetTransition(p, s.Id, v.Id, v.Clips[0].Id, true, "cross_dissolve", 20);
        EditCommands.SetTransition(p, s.Id, v.Id, v.Clips[0].Id, false, "dip_black", 20);
        EditCommands.Split(p, s.Id, v.Id, v.Clips[0].Id, 45);
        Expect(v.Clips[0].TransitionIn is not null && v.Clips[0].TransitionOut is null && v.Clips[1].TransitionIn is null && v.Clips[1].TransitionOut is not null, "split keeps transitions on the outer edges");
        Console.WriteLine("Premiere edit smoke passed: add edit all, Q/W ripple trims, close gap, lift/extract, paste insert, slide, frame hold, transitions.");
    }
}
