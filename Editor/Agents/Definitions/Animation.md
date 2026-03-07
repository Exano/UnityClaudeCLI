---
name: Animation
keywords: [animation, animator, state machine, blend tree, clip, keyframe, timeline, Playable, DOTween, tween, lerp, transition, trigger, parameter, layer, avatar, IK, rig]
---
# Animation
- Use Animator Controllers for character state machines. Keep states minimal and transitions clean.
- Use Animation Events for gameplay hooks (footsteps, VFX triggers).
- For procedural/code-driven animation: prefer Playables API over legacy `Animation` component.
- DOTween/LeanTween for UI and simple transform tweens. Avoid for complex character animation.
- Blend Trees for locomotion (walk/run blending by speed parameter).
- Timeline for cutscenes and scripted sequences. Use Signal Tracks for script callbacks.
