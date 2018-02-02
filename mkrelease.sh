#!/bin/bash

cd `dirname $0` || exit 1

# [assembly: AssemblyVersion ("1.3.1.1")]

version=`sed -n 's/.*\<AssemblyVersion\>.*"\([^"]\+\)".*/\1/p' DockRotate/Properties/AssemblyInfo.cs`
if [ "$version" = "" ]
then
	echo "$0: can't find version number" 1>&2
	exit 1
fi

echo version $version

tmp=`mktemp -d`
trap "rm -rf $tmp" EXIT

echo tmpdir $tmp

