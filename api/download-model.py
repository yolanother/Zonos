from huggingface_hub import hf_hub_download

def download_from_pretrained(repo_id: str, revision: str | None = None):
    config_path = hf_hub_download(repo_id=repo_id, filename="config.json", revision=revision)
    model_path = hf_hub_download(repo_id=repo_id, filename="model.safetensors", revision=revision)
# Load the model
model = download_from_pretrained("Zyphra/Zonos-v0.1-transformer")