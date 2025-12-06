FROM ubuntu:22.04

# Install dependencies
RUN apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y \
    wget \
    curl \
    ca-certificates \
    unzip \
    zsh \
    xorg \
    blender \
    xz-utils \
    xvfb \
    && rm -rf /var/lib/apt/lists/*



# Install Godot
RUN curl -L https://github.com/godotengine/godot/releases/download/4.5.1-stable/Godot_v4.5.1-stable_mono_linux_x86_64.zip -o godot.zip \
    && unzip godot.zip \
    && mv Godot_v4.5.1-stable_mono_linux_x86_64/Godot_*.x86_64 /usr/local/bin/godot \
    && chmod +x /usr/local/bin/godot \
    && mv Godot_v4.5.1-stable_mono_linux_x86_64/GodotSharp /usr/local/bin/GodotSharp \
    && rm -rf Godot_v4.5.1-stable_mono_linux_x86_64 godot.zip
ENV GODOT_VERSION="4.5.1"

# Install Godot export templates
RUN mkdir -p /tmp/godot_templates && \
    curl -L https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/Godot_v${GODOT_VERSION}-stable_mono_export_templates.tpz -o /tmp/godot_templates/templates.tpz && \
    unzip -q /tmp/godot_templates/templates.tpz -d /tmp/godot_templates && \
    mkdir -p /root/.local/share/godot/export_templates/${GODOT_VERSION}.stable.mono && \
    cp -r /tmp/godot_templates/templates/* /root/.local/share/godot/export_templates/${GODOT_VERSION}.stable.mono/ && \
    rm -rf /tmp/godot_templates

ADD platform/setup_editor_settings_version.sh /opt/scripts/setup_editor_settings_version.sh
RUN bash /opt/scripts/setup_editor_settings_version.sh

# Install .NET 8 SDK
RUN wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
RUN chmod +x ./dotnet-install.sh
RUN ./dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet
ENV PATH="/usr/share/dotnet:${PATH}"

# install blender and also setup blender's path in editor settings so that you can use the inbuilt blender importer
ENV BLENDER_VERSION="4.5.1"

ADD platform/install_blender.sh /opt/scripts/install_blender.sh
RUN bash /opt/scripts/install_blender.sh

ENV PATH="/opt/blender:${PATH}"
ADD platform/setup_blender_editor_path.sh /opt/scripts/setup_blender_editor_path.sh
RUN bash /opt/scripts/setup_blender_editor_path.sh
