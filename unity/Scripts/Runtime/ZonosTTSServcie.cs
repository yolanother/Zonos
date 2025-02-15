using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;

namespace DoubTech.ThirdParty.Zonos
{
    /// <summary>
    /// ZonosTTSService processes text input by breaking it into progressively larger
    /// chunks while respecting sentence boundaries, then requests TTS audio
    /// via an HTTP API and plays it back sequentially.
    /// </summary>
    public class ZonosTTSService : MonoBehaviour
    {
        [Tooltip("Maximum text length for each TTS request.")]
        [SerializeField] private int maxTtsLength = 300;

        [Tooltip("Base URL of the TTS server.")]
        [SerializeField] private string ttsUrl = "http://themberchaud:6004/api/generate-audio/";

        [Tooltip("Audio source for playback.")]
        [SerializeField] private AudioSource audioSource;

        [SerializeField] private bool debug = false;

        [Range(0, 1)] public float happiness = 0;
        [Range(0, 1)] public float sadness = 0;
        [Range(0, 1)] public float disgust = 0;
        [Range(0, 1)] public float fear = 0;
        [Range(0, 1)] public float surprise = 0;
        [Range(0, 1)] public float anger = 0;
        [Range(0, 1)] public float other = 0;
        [Range(0, 1)] public float neutral = 1;

        private Queue<QueuedPlayback> audioQueue = new Queue<QueuedPlayback>();
        private Task pendingWebRequest;

        private class QueuedPlayback
        {
            public string text;
            public AudioClip audioClip;
            public bool isLoaded;
            public Action<string> onStartPlayback;
            public Action<string> onStopPlayback;
        }
        
        private bool isPlaying = false;
        private bool isLoading = false;
        private Coroutine playbackCoroutine;

        public bool IsPlaying => isPlaying;
        public bool IsLoading => isLoading;

        private void Log(string message, params object[] args)
        {
            if(debug) Debug.Log(string.Format(message, args));
        }

        public void Speak(string text)
        {
            _ = SpeakAsync(text);
        }

        public void SpeakQueued(string text)
        {
            _ = SpeakQueuedAsync(text);
        }

        public async Task SpeakAsync(string text)
        {
            Stop();
            if(null != pendingWebRequest) await pendingWebRequest;
            await SpeakQueuedAsync(text);
        }

        public async Task<string> SpeakQueuedAsync(string text)
        {
            var completionTask = new TaskCompletionSource<string>();
            ProcessTextAndSpeak(new QueuedPlayback
            {
                text = text,
                onStopPlayback = (text) =>
                {
                    completionTask.SetResult(text);
                },
            });
            return await completionTask.Task;
        }

        public void Stop()
        {
            StopAllCoroutines();
            audioSource.Stop();
            foreach (var clip in audioQueue)
            {
                clip.onStopPlayback?.Invoke(clip.text);
            }
            audioQueue.Clear();
            isPlaying = false;
            isLoading = false;
        }

        private void ProcessTextAndSpeak(QueuedPlayback text)
        {
            List<QueuedPlayback> chunks = ChunkText(text, maxTtsLength);
            chunks.First().onStartPlayback = (t) => text.onStartPlayback?.Invoke(t);
            chunks.Last().onStopPlayback = (t) => text.onStopPlayback?.Invoke(t);

            foreach (QueuedPlayback chunk in chunks)
            {
                StartCoroutine(RequestTTS(chunk));
            }
        }

        private List<QueuedPlayback> ChunkText(QueuedPlayback text, int maxLength)
        {
            List<QueuedPlayback> chunks = new List<QueuedPlayback>();
            string[] lines = text.text.Split('\n');
            
            foreach (string line in lines)
            {
                string[] sentences = Regex.Split(line, @"(?<=[.!?])\s+");

                List<string> currentChunk = new List<string>();
                int currentLength = 0;

                foreach (string sentence in sentences)
                {
                    if (currentLength + sentence.Length <= maxLength)
                    {
                        currentChunk.Add(sentence);
                        currentLength += sentence.Length;
                    }
                    else
                    {
                        if (currentChunk.Count > 0)
                        {
                            chunks.Add(new QueuedPlayback { text = string.Join(" ", currentChunk)});
                            currentChunk.Clear();
                            currentLength = 0;
                        }

                        if (sentence.Length > maxLength)
                        {
                            foreach (var s in BreakLongSentence(sentence, maxLength))
                            {
                                chunks.Add(new QueuedPlayback {text = s});
                            }
                        }
                        else
                        {
                            currentChunk.Add(sentence);
                            currentLength += sentence.Length;
                        }
                    }
                }

                if (currentChunk.Count > 0)
                {
                    chunks.Add(new QueuedPlayback {text = string.Join(" ", currentChunk)});
                }
            }

            return chunks;
        }

        private List<string> BreakLongSentence(string sentence, int maxLength)
        {
            List<string> parts = new List<string>();
            string[] words = sentence.Split(' ');
            List<string> currentPart = new List<string>();
            int currentLength = 0;

            foreach (string word in words)
            {
                if (currentLength + word.Length > maxLength)
                {
                    parts.Add(string.Join(" ", currentPart));
                    currentPart.Clear();
                    currentLength = 0;
                }

                currentPart.Add(word);
                currentLength += word.Length + 1;
            }

            if (currentPart.Count > 0)
            {
                parts.Add(string.Join(" ", currentPart));
            }

            return parts;
        }

        private IEnumerator RequestTTS(QueuedPlayback textChunk)
        {
            if(null != pendingWebRequest) yield return new WaitUntil(() => pendingWebRequest.Status == TaskStatus.RanToCompletion);
            if (string.IsNullOrEmpty(textChunk.text.Trim()))
            {
                textChunk.isLoaded = true;
                yield break;
            }
            var task = new TaskCompletionSource<bool>();
            pendingWebRequest = task.Task;
            isLoading = true;
            string url = $"{ttsUrl}?text={UnityWebRequest.EscapeURL(textChunk.text)}&sample_name=vampire&happiness={happiness}&sadness={sadness}&disgust={disgust}&fear={fear}&surprise={surprise}&anger={anger}&other={other}&neutral={neutral}&vq_score=0.78&fmax=24000&pitch_std=45&speaking_rate=15&dnsmos_ovrl=4&speaker_noised=false";
            
            Log("Requesting: {0}", textChunk.text);
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    textChunk.audioClip = DownloadHandlerAudioClip.GetContent(request);
                    textChunk.audioClip.name = textChunk.text.Substring(0, Mathf.Min(textChunk.text.Length, 10));
                    textChunk.isLoaded = true;
                    Log("Enqueued: {0}", textChunk.text);
                    audioQueue.Enqueue(textChunk);
                }
                else
                {
                    Debug.LogError($"TTS request failed: {request.error}, could not play back {textChunk.text}");
                    task.SetResult(false);
                }
            }

            isLoading = false;
            task.TrySetResult(true);
            
            if (!isPlaying) playbackCoroutine = StartCoroutine(PlayAudioQueue());
        }

        private IEnumerator PlayAudioQueue()
        {
            isPlaying = true;

            while (audioQueue.Count > 0)
            {
                QueuedPlayback clip = audioQueue.Dequeue();
                yield return new WaitUntil(() => clip.isLoaded);
                if (clip.audioClip != null)
                {
                    Log("Playing: {0} {1}", clip.audioClip.ToString(), clip.text);
                    audioSource.clip = clip.audioClip;
                    audioSource.loop = false;
                    audioSource.Play();
                    clip.onStartPlayback?.Invoke(clip.text);

                    while (audioSource.isPlaying)
                    {
                        yield return new WaitForSeconds(clip.audioClip.length - audioSource.time);
                        yield return null;
                    }
                }

                clip.onStopPlayback?.Invoke(clip.text);
            }

            isPlaying = false;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(ZonosTTSService))]
    public class ZonosTTSServiceEditor : Editor
    {
        private string inputText = "";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ZonosTTSService service = (ZonosTTSService)target;
            inputText = EditorGUILayout.TextField("Input Text", inputText);

            if (GUILayout.Button("Speak"))
            {
                service.Speak(inputText);
            }
            if (GUILayout.Button("Speak Queued"))
            {
                service.SpeakQueued(inputText);
            }
        }
    }
#endif
}
