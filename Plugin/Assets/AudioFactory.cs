using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Networking;

namespace Mjslib.AssetSwap
{
    internal enum AudioDecoder
    {
        UnityWebRequest,
        NVorbis,
    }

    internal sealed class AudioFactory
    {
        private readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>(StringComparer.Ordinal);

        // raw paths currently falling through to the original loader
        private readonly HashSet<string> _fallthrough = new HashSet<string>(StringComparer.Ordinal);

        private readonly AudioDecoder _decoder;
        private readonly BepInEx.Logging.ManualLogSource _log;

        public AudioFactory(AudioDecoder decoder, BepInEx.Logging.ManualLogSource log)
        {
            _decoder = decoder;
            _log = log;
        }

        public bool ConsumeFallthrough(string rawPath) => _fallthrough.Remove(rawPath);

        public bool TryReplace(
            string rawPath, string suffix, string normalizedPath, ReplacementEntry entry,
            Il2CppSystem.Action<AudioClip>? onCompleted)
        {
            if (_cache.TryGetValue(normalizedPath, out var cached))
            {
                // re-decode unity-destroyed cached clips
                if (cached != null)
                {
                    onCompleted?.Invoke(cached);
                    return true;
                }
                _cache.Remove(normalizedPath);
            }

            if (_decoder == AudioDecoder.NVorbis)
            {
                var clip = DecodeNVorbis(normalizedPath, entry);
                if (clip == null) return false;

                Store(normalizedPath, clip);
                onCompleted?.Invoke(clip);
                return true;
            }

            // unitywebrequest finishes through its callback
            BeginUnityWebRequest(rawPath, suffix, normalizedPath, entry, onCompleted);
            return true;
        }

        private void BeginUnityWebRequest(
            string rawPath, string suffix, string normalizedPath, ReplacementEntry entry,
            Il2CppSystem.Action<AudioClip>? onCompleted)
        {
            UnityWebRequest? uwr = null;
            try
            {
                var uri = new Uri(entry.FilePath).AbsoluteUri;
                uwr = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.OGGVORBIS);

                // decode the full clip so length is correct
                var handler = uwr.downloadHandler.TryCast<DownloadHandlerAudioClip>();
                if (handler != null) handler.streamAudio = false;

                var request = uwr;
                var op = uwr.SendWebRequest();
                var done = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<AsyncOperation>>(
                    (Action<AsyncOperation>)(_ =>
                        OnUnityWebRequestDone(request, rawPath, suffix, normalizedPath, entry, onCompleted)));
                op.add_completed(done);
            }
            catch (Exception e)
            {
                _log.LogError(
                    $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: {e.Message}");
                uwr?.Dispose();
                RecoverFromUwrFailure(rawPath, suffix, normalizedPath, entry, onCompleted);
            }
        }

        private void OnUnityWebRequestDone(
            UnityWebRequest uwr, string rawPath, string suffix, string normalizedPath,
            ReplacementEntry entry, Il2CppSystem.Action<AudioClip>? onCompleted)
        {
            try
            {
                if (uwr.result != UnityWebRequest.Result.Success)
                {
                    _log.LogError(
                        $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: {uwr.result} {uwr.error}");
                    RecoverFromUwrFailure(rawPath, suffix, normalizedPath, entry, onCompleted);
                    return;
                }

                var clip = DownloadHandlerAudioClip.GetContent(uwr);
                if (clip == null)
                {
                    _log.LogError(
                        $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: empty audio clip");
                    RecoverFromUwrFailure(rawPath, suffix, normalizedPath, entry, onCompleted);
                    return;
                }

                Store(normalizedPath, clip);
                onCompleted?.Invoke(clip);
            }
            catch (Exception e)
            {
                _log.LogError(
                    $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: {e.Message}");
                RecoverFromUwrFailure(rawPath, suffix, normalizedPath, entry, onCompleted);
            }
            finally
            {
                uwr.Dispose();
            }
        }

        // fall back after unitywebrequest decode failure
        private void RecoverFromUwrFailure(
            string rawPath, string suffix, string normalizedPath, ReplacementEntry entry,
            Il2CppSystem.Action<AudioClip>? onCompleted)
        {
            var clip = DecodeNVorbis(normalizedPath, entry);
            if (clip != null)
            {
                Store(normalizedPath, clip);
                onCompleted?.Invoke(clip);
                return;
            }

            Fallthrough(rawPath, suffix, onCompleted);
        }

        private AudioClip? DecodeNVorbis(string normalizedPath, ReplacementEntry entry)
        {
            try
            {
                using var reader = new NVorbis.VorbisReader(entry.FilePath);

                var channels = reader.Channels;
                var sampleRate = reader.SampleRate;
                var totalFloats = reader.TotalSamples * channels;
                if (channels <= 0 || sampleRate <= 0 || totalFloats <= 0 || totalFloats > int.MaxValue)
                {
                    _log.LogError(
                        $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: bad ogg header (channels={channels}, rate={sampleRate}, samples={reader.TotalSamples})");
                    return null;
                }

                var samples = new float[totalFloats];
                var read = 0;
                int n;
                while (read < samples.Length &&
                       (n = reader.ReadSamples(samples, read, samples.Length - read)) > 0)
                {
                    read += n;
                }

                var lengthSamples = read / channels;
                if (lengthSamples <= 0)
                {
                    _log.LogError(
                        $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: decoded no samples");
                    return null;
                }

                // setdata needs all interleaved samples at offset zero
                var interleaved = lengthSamples * channels;
                if (read != interleaved) Array.Resize(ref samples, interleaved);

                var clip = AudioClip.Create("mjslib:" + normalizedPath, lengthSamples, channels, sampleRate, false);
                clip.SetData((Il2CppStructArray<float>)samples, 0);
                return clip;
            }
            catch (Exception e)
            {
                _log.LogError(
                    $"[mjslib] failed to build {entry.FilePath} for {entry.GamePathRaw}: {e.Message}");
                return null;
            }
        }

        // send this path back to the original audio loader
        private void Fallthrough(string rawPath, string suffix, Il2CppSystem.Action<AudioClip>? onCompleted)
        {
            _fallthrough.Add(rawPath);
            try
            {
                AudioLoaderManager.LoadClip(rawPath, suffix, onCompleted);
            }
            finally
            {
                // clean up the marker after a missing re-entry
                _fallthrough.Remove(rawPath);
            }
        }

        private void Store(string normalizedPath, AudioClip clip)
        {
            // hide the clip from unity cleanup
            clip.hideFlags = HideFlags.HideAndDontSave;
            _cache[normalizedPath] = clip;
        }
    }
}
