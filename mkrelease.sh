#!/bin/bash

name=DockRotate

cd `dirname $0` || exit 1

# [assembly: AssemblyVersion ("1.3.1.1")]

version=`sed -n 's/.*\<AssemblyVersion\>.*"\([^"]\+\)".*/\1/p' DockRotate/Properties/AssemblyInfo.cs`
if [ "$version" = "" ]
then
	echo "ABORTING: can't find version number" 1>&2
	exit 1
fi

echo version $version

tmp=`mktemp -d` || exit 1
trap "rm -rf $tmp" EXIT

dir=$tmp/GameData/$name
mkdir -p $dir || exit 1

dll=DockRotate/bin/Release/DockRotate.dll

for f in `find . -name \*.cs`
do
	if [ $f -nt $dll ]
	then
		echo "ABORTING: $f is newer than $dll" 1>&2
		exit 1
	fi
done

echo generating package in $tmp

rm -f Resources/ModuleManager.*.dll
cp ~/KSP/KSP_linux/GameData/ModuleManager.*.dll $dir/.. || exit 1

cp $dll README.md LICENSE.md Resources/* $dir || exit 1

zip=/tmp/$name-$version.zip
rm -f $zip

(
	cd $tmp &&
	zip -r $zip GameData
) || exit 1

echo generated release $zip

