FROM pytorch/pytorch:2.6.0-cuda12.4-cudnn9-devel
RUN pip install uv

RUN apt update && \
    apt install -y espeak-ng && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY . ./
ENV GRADIO_SERVER_PORT=6005
RUN uv pip install --system -e . && uv pip install --system -e .[compile]
