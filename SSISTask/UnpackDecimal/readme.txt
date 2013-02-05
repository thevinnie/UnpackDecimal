UnpackDecimal
===========

SSIS has no provision for converting packed decimal (comp-3) data.

This transform takes a bytes column, and converts to decimal, using a user provided scale

Configure in advanced editor by clicking input columns containing packed decimal values.
Afterwards, go to the input and output tab and set the PackedScale property on the input column
if it is to differ from 0.

This component automatically creates an output column when the input column is selected, then
forbids any change to that column's metadata. The only allowed user edits are on column name and
description, and the scale. All else are rejected.
 
Scale must be between 0 and 28.
The length of the input field must be 14 bytes or fewer. This imposes a limit of 27 digits on the 
converted value. Though 28 digit numbers are supported by the decimal format, they take 15 bytes 
to store, and a 15 byte packed can hold 29 digits, overflowing the decimal at runtime. 
 
Left as an exercise for the student: 
a) add an error output to direct overflows or badly formatted value to.
b) update component to allow 15 bytes in, and reject values with non-zero most significant nibble.

Interesting Features
====================

This component is part of a series of components that illustrate increasingly complex 
behavior, each one exercising a greater proportion of the SSIS object model. If studying 
in order, this component follows ConfigureUnDouble, and precedes UnDoubleOut.

This component was built to provide an introduction to the use of output columns. Also 
illustrated are:

- Binding input to output columns with custom properties.
- Distinguishing upstream columns from each other.
- Operating on DT_BYTES
- Copy and paste support
- Use of Ondeletinginputcolumn. 
- Setusagetype gives you a virtual input. (because new columns won’t be in the input)
- ReinitializeMetadata to clear up referring columns
- Use the input buffer id not the output buffer id, when setting output column values.


Building
========

Building this component requires that you first:

- Install Visual Studio 2005
- Install SQL Server 2005 Integration Services
- Place gacutil.exe (packaged with Visual Studio) on the system path.

The key file isn’t included in the setup, making for a broken project when installed. 
If you try to build, it will fail, saying the .snk file was not found. Fix the build by, 
on the project properties dialog, going to the Signing tab, dropping the “Choose a strong 
name key file” combo, and picking “<New. . .>”. Give it the same name as the missing one, 
and you won't have to adjust any other project settings. 

The project is configured to run a batch to add the component to the GAC after a successful 
build. After building, verify that "Assembly successfully added to the cache" appears in
the output. 

Registering
===========

Before you can use your component in the designer, you have to add it to the toolbox.

Start SQL Server Business Intelligence Development Studio and create a new Integration Services
project. Add a Data Flow task and go to the data flow panel of the Package.dtsx package. Right-
click the toolbox and select "Choose items . . .". Click on the "SSIS Data Flow Items" tab, find
your component, and check it. Click ok and see that the component appears in the toolbox.

Designing with your Component
=============================

Drag the component to the Data Flow Task designer surface to use it. Add sources, destinations,
and and other components as required, and link them together with paths. Double-click to bring
up your component in the Advanced Editor and configure it.

Debugging
=========

To debug runtime features, create a package and configure a dtexec command line in the Visual 
Studio project properties debug panel. The "external program" is "c:\Program Files\Microsoft 
SQL Server\90\DTS\Binn\DTExec.exe". If you were executing a package named "Sample.dtsx" saved 
in your project directory, the command line arguments would be 
	
	/File "..\..\Sample.dtsx"
	
Set breakpoints in runtime methods and use F5 to start debugging.

To debug in design-time methods, set breakpoints and attach to a running instance of the
Business Intelligence Development Studio (devenv.exe). Bring up your component in the advanced
editor and exercise your design-time methods.

If you make a change and rebuild, you must exit and restart Business Intelligence Development 
Studio before your changes take effect.

James Howey
Copyright (c) Microsoft Corporation.  All rights reserved.