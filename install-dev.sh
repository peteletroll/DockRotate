#!/bin/bash

name=DockRotate
ksphome=~/KSP/KSP_linux

debug=1
while getopts r opt
do
	case $opt in
	r)
		debug=0
		;;
	*)
		exit 1
		;;
	esac
done
shift `expr $OPTIND - 1`

tmp=`mktemp -d` || exit 1
trap "rm -rf $tmp" EXIT

zip="$tmp/$name.zip"
./mkrelease.sh -f -z "$zip" || exit 1

gamedata="$ksphome/GameData"
if [ ! -d "$gamedata" ]
then
	echo "$0: $gamedata directory not found" 1>&2
	exit 1
fi

(
	cd "$tmp" &&
	unzip -q "$zip" &&
	cd GameData || exit 1

	echo "removing previous installation..."
	rm -rf "$gamedata/$name" || exit 1

	if [ $debug -ne 0 ]
	then
		find "./$name" -name \*.dll -exec bash -c 'dll="{}"; mv -v "$dll" "${dll%.dll}.releasedll"' \;
		find "./$name" -name \*.debugdll -exec bash -c 'dll="{}"; mv -v "$dll" "${dll%.debugdll}.dll"' \;
	fi

	echo "installing..."
	cp -r "$name" "$gamedata" || exit 1

	echo "done."
) || exit 1

