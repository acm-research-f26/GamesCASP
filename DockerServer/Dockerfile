FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y \
    curl \
    git \
    python3 \
    python3-pip \
    python3-venv \
    && rm -rf /var/lib/apt/lists/*

# Install Ciao
RUN curl https://ciao-lang.org/boot -sSfL | sh -s -- --prebuilt-bin local-install

ENV CIAOROOT=/root/.ciaoroot/v1.25.0-m1
ENV PATH="/root/.ciaoroot/v1.25.0-m1/build/bin:${PATH}"

# Install s(CASP)
RUN eval "$(${CIAOROOT}/build/bin/ciao-env --sh)" && \
    ciao get gitlab.software.imdea.org/ciao-lang/sCASP

# Make Ciao environment available at runtime
ENV PATH="/root/.ciao/build/bin:/root/.ciaoroot/v1.25.0-m1/build/bin:${PATH}"

WORKDIR /app

COPY Server/requirements.txt .
RUN pip3 install --break-system-packages -r requirements.txt

COPY Server/ .

EXPOSE 6767