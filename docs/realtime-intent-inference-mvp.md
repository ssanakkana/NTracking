# Realtime Intent Inference MVP

## Goal

First ship an experiment-oriented pipeline that feeds recent user activity directly into an LLM and observes prediction quality before optimizing for latency or cost.

## Phase 0

- Keep the current collectors, event bus, and SQLite event storage unchanged.
- Add inference handlers as a side path on the event bus.
- Convert each incoming event into an `InferenceSignal` that is easy for an LLM to consume.
- Push signals into an asynchronous queue so collectors are not blocked by model latency.
- Maintain a per-session in-memory state with the latest window/process plus recent raw signals.
- On every new signal, send the full current session context to an OpenAI-compatible chat endpoint.
- Persist every returned prediction into `IntentPredictions` for replay and offline evaluation.

## Why this shape

- It matches the current codebase with minimal disruption.
- It allows pure-LLM experiments immediately.
- It keeps an upgrade path to later optimizations: embeddings, retrieval, state snapshots, confidence calibration, and trigger rules.

## Current data flow

1. Collectors publish typed events.
2. Existing storage handlers write events into `Events`.
3. New inference handlers convert events into `InferenceSignal` items.
4. `RealtimeInferenceWorker` consumes signals asynchronously.
5. The worker updates per-session state and calls the model.
6. The worker stores results in `IntentPredictions` and only treats high-confidence outputs as triggers.

## Deliberate non-goals for this phase

- No latency optimization.
- No embedding similarity lookup.
- No multi-stage reasoning.
- No accuracy evaluation loop.
- No persistent session-state recovery.

## Evolution path

1. Add richer structured output alongside natural-language explanations.
2. Add replay tooling over `Events` and `IntentPredictions`.
3. Add embedding-based similarity retrieval for relevant historical context.
4. Replace full-context prompting with `new events + cached state + retrieved history`.
5. Split collector, inference, and query into separate local processes with explicit IPC.