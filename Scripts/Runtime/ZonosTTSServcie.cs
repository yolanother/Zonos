using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

        [Range(0, 1)] public float happiness = 0;
        [Range(0, 1)] public float sadness = 0;
        [Range(0, 1)] public float disgust = 0;
        [Range(0, 1)] public float fear = 0;
        [Range(0, 1)] public float surprise = 0;
        [Range(0, 1)] public float anger = 0;
        [Range(0, 1)] public float other = 0;
        [Range(0, 1)] public float neutral = 1;

        private Queue<AudioClip> audioQueue = new Queue<AudioClip>();
        private bool isPlaying = false;

        public void Speak(string text)
        {
            StopCurrentPlayback();
            StartCoroutine(ProcessTextAndSpeak(text));
        }

        public void SpeakQueued(string text)
        {
            StartCoroutine(ProcessTextAndSpeak(text));
        }

        private void StopCurrentPlayback()
        {
            audioSource.Stop();
            audioQueue.Clear();
            isPlaying = false;
        }

        private IEnumerator ProcessTextAndSpeak(string text)
        {
            List<string> chunks = ChunkText(text, maxTtsLength);

            foreach (string chunk in chunks)
            {
                yield return StartCoroutine(RequestTTS(chunk));
            }
            
            if (!isPlaying) StartCoroutine(PlayAudioQueue());
        }

        private List<string> ChunkText(string text, int maxLength)
        {
            List<string> chunks = new List<string>();
            string[] lines = text.Split('\n');
            
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
                            chunks.Add(string.Join(" ", currentChunk));
                            currentChunk.Clear();
                            currentLength = 0;
                        }

                        if (sentence.Length > maxLength)
                        {
                            chunks.AddRange(BreakLongSentence(sentence, maxLength));
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
                    chunks.Add(string.Join(" ", currentChunk));
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

        private IEnumerator RequestTTS(string textChunk)
        {
            string url = $"{ttsUrl}?text={UnityWebRequest.EscapeURL(textChunk)}&sample_name=vampire&happiness={happiness}&sadness={sadness}&disgust={disgust}&fear={fear}&surprise={surprise}&anger={anger}&other={other}&neutral={neutral}&vq_score=0.78&fmax=24000&pitch_std=45&speaking_rate=15&dnsmos_ovrl=4&speaker_noised=false";
            
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.WAV))
            {
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
                    audioQueue.Enqueue(clip);
                }
                else
                {
                    Debug.LogError($"TTS request failed: {request.error}");
                }
            }
        }

        private IEnumerator PlayAudioQueue()
        {
            isPlaying = true;

            while (audioQueue.Count > 0)
            {
                AudioClip clip = audioQueue.Dequeue();
                audioSource.clip = clip;
                audioSource.Play();
                yield return new WaitForSeconds(clip.length);
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
