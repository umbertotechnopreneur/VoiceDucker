/* VBWR B
 * Project: VoiceDucker
 * Repository: https://github.com/umbertotechnopreneur/VoiceDucker
 * Creator: Umberto Giacobbi | https://umbertogiacobbi.biz
 * VibeWare initiative: Human intent. AI implementation. Accountable human review.
 * Manifesto: https://umbertogiacobbi.biz/vibeware/manifesto
 * Created with AI: OpenAI Codex assisted the initial implementation; Git history
 * Human guidance: Umberto Giacobbi defined the purpose and audio behavior
 * Evidence: Git history
 * Copyright (c) 2026 Umberto Giacobbi
 * License: MIT
 * VBWR E */

namespace VoiceDucker.Audio;

public sealed record EngineStatus(
    bool Running,
    bool Speaking,
    int AffectedSessions,
    double MicrophoneLevel,
    string Message);
