#!/bin/bash

name=DockRotate
ksphome=~/KSP/KSP_linux

debug=0
while getopts d opt
do
	case $opt in
	d)
		debug=1
		;;
	*)
		exit 1
		;;
	esac
done
shift `expr $OPTIND - 1`

tmp=`mktemp -d` || exit 1
trap "rm -rf $tmp" EXIT

zip="$tmp/dr.zip"
./mkrelease.sh -f -z "$zip" || exit 1

gamedata="$ksphome/GameData"
if [ ! -d "$gamedata" ]
then
	echo "$0: $gamedata directory not found" 1>&2
	exit 1
fi

(
	cd "$tmp" &&
	unzip "$zip" &&
	cd GameData || exit 1

	if [ $debug -ne 0 ]
	then
		echo
		find . -name \*.debugdll -exec bash -c 'dll="{}"; mv -v "$dll" "${dll%.debugdll}.dll"' \;
	fi

	echo
	rm -rfv "$gamedata/$name" || exit 1
	echo
	cp -rv "$name" "$gamedata" || exit 1

	echo
) || exit 1


