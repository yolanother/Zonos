"""
requirements.txt:
fastapi
uvicorn
torch
torchaudio
pydub
zonos
"""

from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.responses import FileResponse
import torch
import torchaudio
import os
import uvicorn
import re
from zonos.model import Zonos
from zonos.conditioning import make_cond_dict
from pydub import AudioSegment

# Initialize FastAPI app
app = FastAPI(title="Audio Generation API", openapi_url="/api/docs/openapi.json", docs_url="/api/docs")

# Load the model
model = Zonos.from_pretrained("Zyphra/Zonos-v0.1-transformer", device="cuda")

# Ensure output directories exist
OUTPUT_DIR = "/data/generated_audio"
SAMPLES_DIR = "/data/samples"
os.makedirs(OUTPUT_DIR, exist_ok=True)
os.makedirs(SAMPLES_DIR, exist_ok=True)

def sanitize_filename(filename: str) -> str:
    """
    Remove invalid characters from filenames to prevent path traversal or injection attacks.
    """
    return re.sub(r'[^a-zA-Z0-9_-]', '_', filename)

def convert_to_wav(input_path: str, output_path: str):
    """
    Convert audio file to WAV format.
    """
    audio = AudioSegment.from_file(input_path)
    audio = audio.set_frame_rate(16000).set_channels(1).set_sample_width(2)
    audio.export(output_path, format="wav")

@app.post("/api/upload-sample/")
async def upload_sample(sample_name: str, audio_file: UploadFile = File(...)):
    """
    Upload a named sample audio file to be used for speaker embedding.
    Supports MP3, M4A, and WAV formats.
    """
    try:
        sample_name = sanitize_filename(sample_name)
        temp_path = f"{SAMPLES_DIR}/{sample_name}.{audio_file.filename.split('.')[-1]}"
        output_path = f"{SAMPLES_DIR}/{sample_name}.wav"
        
        with open(temp_path, "wb") as buffer:
            buffer.write(await audio_file.read())
        
        # Convert to WAV if needed
        if not temp_path.endswith(".wav"):
            convert_to_wav(temp_path, output_path)
            os.remove(temp_path)
        else:
            os.rename(temp_path, output_path)
        
        return {"message": "Sample uploaded and converted successfully", "sample_name": sample_name}
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/api/generate-audio/")
async def generate_audio(
    text: str,
    sample_name: str = None,
    happiness: float = 0.0,
    sadness: float = 0.0,
    disgust: float = 0.0,
    fear: float = 0.0,
    surprise: float = 0.0,
    anger: float = 0.0,
    other: float = 0.0,
    neutral: float = 1.0,
    vq_score: float = 0.78,
    fmax: int = 24000,
    pitch_std: float = 45.0,
    speaking_rate: float = 15.0,
    dnsmos_ovrl: float = 4.0,
    speaker_noised: bool = False,
    audio_file: UploadFile = File(None)
):
    """
    Generate audio from text using a provided or uploaded sample for speaker embedding.
    Accepts optional emotion values and additional tuning parameters.
    Returns a generated audio file.
    """
    try:
        if sample_name:
            sample_name = sanitize_filename(sample_name)
            input_audio_path = f"{SAMPLES_DIR}/{sample_name}.wav"
            if not os.path.exists(input_audio_path):
                raise HTTPException(status_code=404, detail="Sample not found")
        elif audio_file:
            temp_path = f"{OUTPUT_DIR}/{audio_file.filename}"
            output_path = f"{OUTPUT_DIR}/converted.wav"
            
            with open(temp_path, "wb") as buffer:
                buffer.write(await audio_file.read())
            
            # Convert to WAV if necessary
            if not temp_path.endswith(".wav"):
                convert_to_wav(temp_path, output_path)
                os.remove(temp_path)
                input_audio_path = output_path
            else:
                os.rename(temp_path, output_path)
                input_audio_path = output_path
        else:
            raise HTTPException(status_code=400, detail="Either sample_name or audio_file must be provided")

        # Load audio for speaker embedding
        wav, sampling_rate = torchaudio.load(input_audio_path)
        speaker = model.make_speaker_embedding(wav, sampling_rate)

        # Set default emotion values
        emotion_values = [happiness, sadness, disgust, fear, surprise, anger, other, neutral]
        emotion_tensor = torch.tensor(emotion_values, device="cuda")

        # Prepare conditioning
        cond_dict = make_cond_dict(
            text=text,
            speaker=speaker,
            language="en-us",
            emotion=emotion_tensor,
            vqscore_8=torch.tensor([vq_score] * 8, device="cuda").unsqueeze(0),
            fmax=fmax,
            pitch_std=pitch_std,
            speaking_rate=speaking_rate,
            dnsmos_ovrl=dnsmos_ovrl,
            speaker_noised=speaker_noised,
        )
        conditioning = model.prepare_conditioning(cond_dict)

        # Generate audio
        codes = model.generate(conditioning)
        wavs = model.autoencoder.decode(codes).cpu()

        # Save generated audio
        output_audio_path = f"{OUTPUT_DIR}/output.wav"
        torchaudio.save(output_audio_path, wavs[0], model.autoencoder.sampling_rate)

        return FileResponse(output_audio_path, media_type="audio/wav", filename="output.wav")

    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=6004)
