FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

# Basic dependencies
RUN apt-get update && apt-get install -y \
    curl \
    git \
    python3 \
    python3-pip \
    python3-venv \
    && rm -rf /var/lib/apt/lists/*

# Install Ciao Prolog
RUN curl https://ciao-lang.org/boot -sSfL | sh

# Make Ciao available
ENV PATH="/root/.ciao/bin:${PATH}"

# Install s(CASP)
RUN ciao get gitlab.software.imdea.org/ciao-lang/sCASP

WORKDIR /app

# Python dependencies
COPY requirements.txt .

RUN pip3 install --break-system-packages -r requirements.txt

# Copy server
COPY Server/ .

# Expose WebSocket server
EXPOSE 6767

CMD ["python3", "server.py"]