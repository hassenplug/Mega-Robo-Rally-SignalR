#!/bin/bash

######################
# Setup process
######################

## 10/9/23
# Write image to SD card
#   enable ssh & configure network
# Boot Pi
# ssh into computer
# run these commands
######
# sudo apt install git
# git clone https://github.com/hassenplug/Mega-Robo-Rally.git
#
# /home/mrr/Mega-Robo-Rally/install/git.sh
#######
#
#D) sudo raspi-config => enable auto  log to command line
#G) Enable remote access for database
#H) Enable remote commands (reboot) ?? not working
# configure to connect to db from outside source

######################
# Update OS
######################
sudo apt-get update -y
sudo apt-get upgrade -y

######################
# Configure items
######################


######################
# list of apps to install
######################

#Install MySQL
sudo apt-get install mariadb-server -y 	# lamp   

#Install dotnet
curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel STS

echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$PATH:$HOME/.dotnet' >> ~/.bashrc
source ~/.bashrc

#connector for mysql
dotnet add package MySqlConnector --version 2.2.7

######################
# configure database
######################
# run sql file
sudo mysql < /home/Mega-Robo-Rally/install/SRRDatabase.sql
sudo mysql rally < /home/Mega-Robo-Rally/install/rallyBoards.sql

######################
# create startup file
######################
# copy files to other locations
##cp /boot/InstallFiles/startup.sh /.config/lxsession/LXDE/autostart

#sudo cp /boot/InstallFiles/startup.sh /etc/init.d/
#cd /etc/init.d/
#sudo chmod +x startup.sh
#sudo update-rc.d startup.sh defaults

######################
# connect to db from remote
######################
# sudo nano /etc/mysql/mariadb.conf.d/50-server.cnf
## comment out
# bind-address=127.0.0.1

### endable spi
# sudo nano /boot/firmware/config.txt
### uncomment:
# dtparam=spi=on.

dotnet add package System.Device.Gpio --source https://pkgs.dev.azure.com/dotnet/IoT/_packaging/nightly_iot_builds/nuget/v3/index.json
dotnet add package Iot.Device.Bindings --source https://pkgs.dev.azure.com/dotnet/IoT/_packaging/nightly_iot_builds/nuget/v3/index.json
dotnet add package Microsoft.AspNetCore.SignalR

#sudo reboot

