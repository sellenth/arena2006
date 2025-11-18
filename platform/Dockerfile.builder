FROM ubuntu:22.04

# Install dependencies
RUN apt-get update && apt-get install -y \
    wget \
    unzip \
    zsh \
    xorg \
    blender \
    xz-utils \
    xvfb \
    && rm -rf /var/lib/apt/lists/*



# Install Godot
RUN wget https://github.com/godotengine/godot/releases/download/4.5.1-stable/Godot_v4.5.1-stable_mono_linux_x86_64.zip -O godot.zip
RUN unzip godot.zip && mv Godot_v4.5.1-stable_mono_linux_x86_64 /usr/local/bin/godot &&  mv GodotSharp /usr/local/bin/godot/GodotSharp && rm godot.zip
ENV GODOT_VERSION="4.5.1"

ADD setup_editor_settings_version.sh /opt/scripts/setup_editor_settings_version.sh
RUN bash /opt/scripts/setup_editor_settings_version.sh

# Install .NET 8 SDK
RUN wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
RUN chmod +x ./dotnet-install.sh
RUN ./dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet
ENV PATH="/usr/share/dotnet:${PATH}"

# install blender and also setup blender's path in editor settings so that you can use the inbuilt blender importer
ENV BLENDER_VERSION="4.5.1"

ADD install_blender.sh /opt/scripts/install_blender.sh
RUN bash /opt/scripts/install_blender.sh

ENV PATH="/opt/blender:${PATH}"
ADD setup_blender_editor_path.sh /opt/scripts/setup_blender_editor_path.sh
RUN bash /opt/scripts/setup_blender_editor_path.sh