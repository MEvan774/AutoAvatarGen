Voice lab fixtures for the Voice_* self-tests (TtsPipelineSelfTests.Voice.cs).

Three real ElevenLabs takes of the same 249-word passage, same voice, made on
2026-11-17 (MugsTech repo, Videos/_voice_lab/out):

  v4_take1    eleven_v4, the v3-sized cues     out/2026-11-17_13-26-39/v4_take1
  v4_clip_C   eleven_v4, the short v4 cues     out/2026-11-17_18-45-02/clip_C
  v3_take1    eleven_v3                        out/2026-11-17_13-26-39/v3_take1

<name>.wavdata         the loudness-matched take (-16 LUFS, as the pipeline
                       slices it), down-mixed to 16 kHz mono 16-bit WAV. Named
                       .wavdata because .gitignore drops *.wav.
<name>_alignment.json  the API's `alignment` object, untouched. Its characters,
                       joined, are the exact text that was sent.

The folder name ends in ~ so Unity does not import it.
