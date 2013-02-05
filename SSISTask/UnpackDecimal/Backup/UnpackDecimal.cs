//UnpackDecimal
//===========

//SSIS has no provision for converting packed decimal (comp-3) data.

//This transform takes a bytes column, and converts to decimal, using a user provided scale

//Configure in advanced editor by clicking input columns containing packed decimal values.
//Afterwards, go to the input and output tab and set the PackedScale property on the input column
//if it is to differ from 0.

//This component automatically creates an output column when the input column is selected, then
//forbids any change to that column's metadata. The only allowed user edits are on column name and
//description, and the scale. All else are rejected.
 
//Scale must be between 0 and 28.
//The length of the input field must be 14 bytes or fewer. This imposes a limit of 27 digits on the 
//converted value. Though 28 digit numbers are supported by the decimal format, they take 15 bytes 
//to store, and a 15 byte packed can hold 29 digits, overflowing the decimal at runtime. 
 
//Left as an exercise for the student: 
//a) add an error output to direct overflows or badly formatted value to.
//b) update component to allow 15 bytes in, and reject values with non-zero most significant nibble.

//Interesting Features
//====================

//This component is part of a series of components that illustrate increasingly complex 
//behavior, each one exercising a greater proportion of the SSIS object model. If studying 
//in order, this component follows ConfigureUnDouble, and precedes UnDoubleOut.

//This component was built to provide an introduction to the use of output columns. Also 
//illustrated are:

//- Binding input to output columns with custom properties.
//- Distinguishing upstream columns from each other.
//- Operating on DT_BYTES
//- Copy and paste support
//- Use of Ondeletinginputcolumn. 
//- Setusagetype gives you a virtual input. (because new columns won’t be in the input)
//- ReinitializeMetadata to clear up referring columns
//- Use the input buffer id not the output buffer id, when setting output column values.

// By James Howey
// Copyright (c) Microsoft Corporation.  All rights reserved.

using System;
using Microsoft.SqlServer.Dts.Pipeline;
using Microsoft.SqlServer.Dts.Pipeline.Wrapper;
using Microsoft.SqlServer.Dts.Runtime.Wrapper;
using Microsoft.SqlServer.Dts;
using Microsoft.SqlServer.Dts.ManagedMsg;

using System.Diagnostics;


namespace CustomComponents
{
    [DtsPipelineComponent(DisplayName = "UnpackDecimal", ComponentType = ComponentType.Transform)]
    public class UnpackDecimalComponent : PipelineComponent
    {
        #region helper methods and objects

        private void PostError(string message)
        {
            bool cancel = false;
            this.ComponentMetaData.FireError(0, this.ComponentMetaData.Name, message, "", 0, out cancel);
        }

        private DTSValidationStatus promoteStatus(ref DTSValidationStatus currentStatus, DTSValidationStatus newStatus)
        {
            // statuses are ranked in order of increasing severity, from
            //   valid to broken to needsnewmetadata to corrupt.
            // bad status, if any, is result of programming error
            switch (currentStatus)
            {
                case DTSValidationStatus.VS_ISVALID:
                    switch (newStatus)
                    {
                        case DTSValidationStatus.VS_ISBROKEN:
                        case DTSValidationStatus.VS_ISCORRUPT:
                        case DTSValidationStatus.VS_NEEDSNEWMETADATA:
                            currentStatus = newStatus;
                            break;
                        case DTSValidationStatus.VS_ISVALID:
                            break;
                        default:
                            throw new System.ApplicationException("Internal Error: A value outside the scope of the status enumeration was found.");
                    }
                    break;
                case DTSValidationStatus.VS_ISBROKEN:
                    switch (newStatus)
                    {
                        case DTSValidationStatus.VS_ISCORRUPT:
                        case DTSValidationStatus.VS_NEEDSNEWMETADATA:
                            currentStatus = newStatus;
                            break;
                        case DTSValidationStatus.VS_ISVALID:
                        case DTSValidationStatus.VS_ISBROKEN:
                            break;
                        default:
                            throw new System.ApplicationException("Internal Error: A value outside the scope of the status enumeration was found.");
                    }
                    break;
                case DTSValidationStatus.VS_NEEDSNEWMETADATA:
                    switch (newStatus)
                    {
                        case DTSValidationStatus.VS_ISCORRUPT:
                            currentStatus = newStatus;
                            break;
                        case DTSValidationStatus.VS_ISVALID:
                        case DTSValidationStatus.VS_ISBROKEN:
                        case DTSValidationStatus.VS_NEEDSNEWMETADATA:
                            break;
                        default:
                            throw new System.ApplicationException("Internal Error: A value outside the scope of the status enumeration was found.");
                    }
                    break;
                case DTSValidationStatus.VS_ISCORRUPT:
                    switch (newStatus)
                    {
                        case DTSValidationStatus.VS_ISCORRUPT:
                        case DTSValidationStatus.VS_ISVALID:
                        case DTSValidationStatus.VS_ISBROKEN:
                        case DTSValidationStatus.VS_NEEDSNEWMETADATA:
                            break;
                        default:
                            throw new System.ApplicationException("Internal Error: A value outside the scope of the status enumeration was found.");
                    }
                    break;
                default:
                    throw new System.ApplicationException("Internal Error: A value outside the scope of the status enumeration was found.");
            }
            return currentStatus;
        } 
        #endregion

        #region design time functionality

        public override DTSValidationStatus Validate()
        {
            // if scale doesn't match output column, or extra output columns, or insufficient
            // output columns, this component is corrupt. We can say this because in our design
            // time methods, we have compelled the output columns to remain in lockstep with 
            // input column selections

            // if I had numerics, I would have to worry about matching the precision of the numeric
            // to the length of the packed field, but decimal is fixed length, so all that changes
            // is scale.

            // check for:
            //  basic layout validation
            //  orphaned input columns

            //  if component is corrupt, we are permitted to return without further checks
            DTSValidationStatus status = base.Validate();
            if (status == DTSValidationStatus.VS_ISCORRUPT)
            {
                return status;
            }

            IDTSComponentMetaData90 metadata = this.ComponentMetaData;
            IDTSCustomProperty90 customProperty;
            int lineageID;

            
            IDTSInputCollection90 inputCollection = metadata.InputCollection;
            IDTSInput90 input = inputCollection[0];
            IDTSInputColumnCollection90 inputColumnCollection = input.InputColumnCollection;
            IDTSInputColumn90 inputColumn;

            IDTSOutputCollection90 outputCollection = metadata.OutputCollection;
            IDTSOutput90 output = outputCollection[0];
            IDTSOutputColumnCollection90 outputColumnCollection = output.OutputColumnCollection;
            IDTSOutputColumn90 outputColumn;

            if (inputColumnCollection.Count != outputColumnCollection.Count)
            {
                PostError("Input and output columns don't match up.");
                return DTSValidationStatus.VS_ISCORRUPT;
            }

            for (int j = 0; j < outputColumnCollection.Count; j++)
            {
                outputColumn = outputColumnCollection[j];
                lineageID = outputColumn.LineageID;
                IDTSCustomPropertyCollection90 customPropertyCollection = outputColumn.CustomPropertyCollection;
                try
                {
                    customProperty = customPropertyCollection["InputColumnID"];
                }
                catch (Exception)
                {
                    PostError("Output column [" + outputColumn.Name + "] has no InputColumnID custom property.");
                    return DTSValidationStatus.VS_ISCORRUPT;
                }

                int inputColumnID = (int)customProperty.Value;
                try
                {
                    inputColumn = inputColumnCollection.FindObjectByID(inputColumnID);
                }
                catch (Exception)
                {
                    PostError("InputColumnID [" + inputColumnID + "] not found in selected input columns.");
                    return DTSValidationStatus.VS_ISCORRUPT;
                }
                // if input column is orphaned, we already have reinit status, and cleanup will 
                // eliminate any following errors. 
                if (inputColumn.IsValid)
                {
                    if (inputColumn.DataType != DataType.DT_BYTES)
                    {
                        PostError("Column [" + inputColumn.Name + "] is not a DT_BYTES column");
                        promoteStatus(ref status, DTSValidationStatus.VS_ISBROKEN);
                    }
                    IDTSCustomPropertyCollection90 inputColumnCustomProperties = inputColumn.CustomPropertyCollection;
                    try
                    {
                        customProperty = inputColumnCustomProperties["PackedScale"];
                    }
                    catch (Exception)
                    {
                        PostError("InputColumnID [" + inputColumnID + "] has no PackedScale property");
                        return DTSValidationStatus.VS_ISCORRUPT;
                    }
                    int packedScale = (int)customProperty.Value;
                    if (packedScale < 0 || packedScale > 28)
                    {
                        PostError("PackedScale must be between 0 and 28.");
                        return DTSValidationStatus.VS_ISCORRUPT;
                    }
                    if (outputColumn.DataType != DataType.DT_DECIMAL)
                    {
                        PostError("Output column data type must be decimal");
                        return DTSValidationStatus.VS_ISCORRUPT;
                    }
                    if (outputColumn.Scale != packedScale)
                    {
                        PostError("PackedScale must match output column scale.");
                        return DTSValidationStatus.VS_ISCORRUPT;
                    }
                }
            }
            return status;
        }

        public override void ReinitializeMetaData()
        {
            // This should delete orphaned columns for us, calling OnDeletingInputColumn
            base.ReinitializeMetaData();
        }

        public override IDTSInput90 InsertInput(DTSInsertPlacement insertPlacement, int inputID)
        {
            PostError("Component requires exactly one input. New input is forbidden.");
            throw new PipelineComponentHResultException(HResults.DTS_E_CANTADDINPUT);
        }

        public override void DeleteInput(int inputID)
        {
            PostError("Component requires exactly one input. Deleted input is forbidden.");
            throw new PipelineComponentHResultException(HResults.DTS_E_CANTDELETEINPUT);
        }

        public override IDTSOutput90 InsertOutput(DTSInsertPlacement insertPlacement, int outputID)
        {
            PostError("Component requires exactly one output. New output is forbidden.");
            throw new PipelineComponentHResultException(HResults.DTS_E_CANTADDOUTPUT);
        }

        public override void DeleteOutput(int outputID)
        {
            PostError("Component requires exactly one output. Deleted output is forbidden.");
            throw new PipelineComponentHResultException(HResults.DTS_E_CANTDELETEOUTPUT);
        }

        public override IDTSOutputColumn90 InsertOutputColumnAt(int outputID, int outputColumnIndex, string name, string description)
        {
            PostError("Component forbids adding output columns. Check input columnn to configure.");
            throw new PipelineComponentHResultException(HResults.DTS_E_CANTADDCOLUMN);
        }

        public override void DeleteOutputColumn(int outputID, int outputColumnID)
        {
            PostError("Component forbids deleting output columns. Uncheck input columnn to configure.");
            throw new PipelineComponentHResultException(HResults.DTS_E_CANTADDCOLUMN);
        }
        
        // done
        public override void  OnDeletingInputColumn(int inputID, int inputColumnID)
        {
			IDTSComponentMetaData90 metadata = this.ComponentMetaData;

			// An input column is being deleted. This may be because it no longer is 
			// present upstream, or because the user has unchecked it.
			// We have to retrieve the column, then remove any 
			// reference to it in the output column collection
			IDTSInputCollection90 inputCollection = metadata.InputCollection;
    		IDTSInput90 input = inputCollection[0];
			// Get input column collection and retrieve interesting column
			IDTSInputColumnCollection90 inputColumnCollection = input.InputColumnCollection;
            IDTSInputColumn90 inputColumn = inputColumnCollection.FindObjectByID(inputColumnID);

			// Iterate over output columns looking for matching id in custom property.
			IDTSOutputCollection90 outputCollection = metadata.OutputCollection;
			IDTSOutput90 output = outputCollection[0];
			IDTSOutputColumnCollection90 outputColumnCollection = output.OutputColumnCollection;
			for (int j = 0; j < outputColumnCollection.Count; j++)
			{
				IDTSOutputColumn90 outputColumn = outputColumnCollection[j];
				IDTSCustomPropertyCollection90 customPropertyCollection = outputColumn.CustomPropertyCollection;
				IDTSCustomProperty90 customProperty = customPropertyCollection["InputColumnID"];
				int columnID = (int)customProperty.Value;
				if (columnID == inputColumnID)
				{
                    // we just delete the output column
                    base.DeleteOutputColumn(output.ID, outputColumn.ID);
				}
			}
        }
        
        // done
        public override IDTSInputColumn90 SetUsageType(int inputID, IDTSVirtualInput90 virtualInput, int lineageID, DTSUsageType usageType)
        {
            IDTSInputColumn90 inputColumn;
            IDTSComponentMetaData90 metadata = this.ComponentMetaData;

            switch (usageType)
            {
                case DTSUsageType.UT_READONLY:
			        IDTSVirtualInputColumn90 column = virtualInput.VirtualInputColumnCollection.GetVirtualInputColumnByLineageID(lineageID);
                    if (column.DataType != DataType.DT_BYTES)
                    {
                        PostError("Component operates only on bytes input. Other types are forbidden.");
                        throw new PipelineComponentHResultException(HResults.DTS_E_CANTSETUSAGETYPE);
                    }
                    else
                    {
                        if (column.Length > 14)
                        {
                            PostError("Component accepts a maximum field length of 14.");
                            throw new PipelineComponentHResultException(HResults.DTS_E_CANTSETUSAGETYPE);
                        }
                        else
                        {
                            inputColumn = base.SetUsageType(inputID, virtualInput, lineageID, usageType);
                            IDTSCustomPropertyCollection90 customProperties = inputColumn.CustomPropertyCollection;
                            IDTSCustomProperty90 customProperty = customProperties.New();
                            customProperty.Name = "PackedScale"; // do not localize
                            customProperty.ContainsID = false;
                            customProperty.Value = 0; // default is zero for scale 

                            IDTSOutputCollection90 outputCollection = metadata.OutputCollection;
                            IDTSOutput90 output = outputCollection[0];
                            IDTSOutputColumnCollection90 outputColumnCollection = output.OutputColumnCollection;
                            // this will generate a unique name, because upstream component names can't have dots int them
                            IDTSOutputColumn90 newColumn = base.InsertOutputColumnAt(output.ID, outputColumnCollection.Count,
                                inputColumn.UpstreamComponentName + "." + inputColumn.Name + ".Decimal", "");
                            newColumn.SetDataTypeProperties(DataType.DT_DECIMAL, 0, 0, 0, 0);

                            customProperties = newColumn.CustomPropertyCollection;
                            customProperty = customProperties.New();
                            customProperty.Name = "InputColumnID"; // do not localize
                            // support cut and paste
                            customProperty.ContainsID = true;
                            customProperty.Value = inputColumn.ID;

                            return inputColumn;
                        }
                    }
                case DTSUsageType.UT_READWRITE:
                    PostError("Component requires that input columns be marked read only.");
                    throw new PipelineComponentHResultException(HResults.DTS_E_CANTSETUSAGETYPE);
                case DTSUsageType.UT_IGNORED:
                    IDTSInputCollection90 inputCollection = metadata.InputCollection;
                    IDTSInput90 input = inputCollection[0];
                    IDTSInputColumnCollection90 inputColumnCollection = input.InputColumnCollection;
                    inputColumn = inputColumnCollection.GetInputColumnByLineageID(lineageID);
					this.OnDeletingInputColumn(inputID, inputColumn.ID);
                    inputColumn = base.SetUsageType(inputID, virtualInput, lineageID, usageType);
                    return inputColumn;
                default:
                    throw new PipelineComponentHResultException(HResults.DTS_E_CANTSETUSAGETYPE);
            }
        }

        public override IDTSCustomProperty90 SetInputColumnProperty(int inputID, int inputColumnID, string propertyName, object propertyValue)
        {
            if (propertyName == "PackedScale")
            {
                int value = (int)propertyValue;
                // scale ranges from 0 to 28
                if (value >= 0 && value <= 28)
                {
                    IDTSComponentMetaData90 metadata = this.ComponentMetaData;
                    IDTSOutputCollection90 outputCollection = metadata.OutputCollection;
                    IDTSOutput90 output = outputCollection[0];
                    IDTSOutputColumnCollection90 outputColumnCollection = output.OutputColumnCollection;
                    IDTSOutputColumn90 outputColumn;
                    for (int j = 0; j < outputColumnCollection.Count; j++)
                    {
                        outputColumn = outputColumnCollection[j];
                        IDTSCustomPropertyCollection90 customPropertyCollection = outputColumn.CustomPropertyCollection;
                        IDTSCustomProperty90 customProperty = customPropertyCollection["InputColumnID"];
                        int linkedInputID = (int)customProperty.Value;
                        if (linkedInputID == inputColumnID)
                        {
                            outputColumn.SetDataTypeProperties(DataType.DT_DECIMAL,0,0,value,0);
                            return base.SetInputColumnProperty(inputID, inputColumnID, propertyName, propertyValue);
                        }
                    }
			        PostError("Couldn't find matching output column. Component likely corrupt.");
			        throw new PipelineComponentHResultException(HResults.DTS_E_FAILEDTOSETPROPERTY);
                }
                else
                {
			        PostError("PackedScale must be between 0 and 28.");
			        throw new PipelineComponentHResultException(HResults.DTS_E_FAILEDTOSETPROPERTY);
                }
            }
            else
            {
			    PostError("Unexpected property name to set.");
			    throw new PipelineComponentHResultException(HResults.DTS_E_FAILEDTOSETPROPERTY);
            }
        }

        
#endregion

        #region private member variables

        private int[] outColumnWriteIDs;
        private int[] outColumnSourceIDs;
        private int[] outColumnScale;
        private int numOutColumnWrites = 0;

        #endregion

        #region runtime functionality
        public override void PreExecute()
        {
            IDTSCustomProperty90 customProperty;
            int lineageID;

            IDTSComponentMetaData90 metadata = this.ComponentMetaData;
            
            IDTSInput90 input = this.ComponentMetaData.InputCollection[0];
            int inputBufferID = input.Buffer;
            IDTSInputColumnCollection90 inputColumnCollection = input.InputColumnCollection;
            IDTSInputColumn90 inputColumn;
            IDTSOutputCollection90 outputCollection = metadata.OutputCollection;
            IDTSOutput90 output = outputCollection[0];
            IDTSOutputColumnCollection90 outputColumnCollection = output.OutputColumnCollection;
            IDTSOutputColumn90 outputColumn;

            // get output columns to write
            this.outColumnWriteIDs = new int[outputColumnCollection.Count];
            this.outColumnSourceIDs = new int[outputColumnCollection.Count];
            this.outColumnScale = new int[outputColumnCollection.Count];
            this.numOutColumnWrites = 0;
            for (int j = 0; j < outputColumnCollection.Count; j++)
            {
                outputColumn = outputColumnCollection[j];
                lineageID = outputColumn.LineageID;
                // this.numOutColumnWrites index is incremented farther below.
                this.outColumnWriteIDs[this.numOutColumnWrites] = this.BufferManager.FindColumnByLineageID(inputBufferID, lineageID);
                IDTSCustomPropertyCollection90 customPropertyCollection = outputColumn.CustomPropertyCollection;
                customProperty = customPropertyCollection["InputColumnID"];
                int inputID = (int)customProperty.Value;
                inputColumn = inputColumnCollection.FindObjectByID(inputID);
                this.outColumnSourceIDs[this.numOutColumnWrites] = this.BufferManager.FindColumnByLineageID(inputBufferID, inputColumn.LineageID);
                IDTSCustomPropertyCollection90 inputColumnCustomProperties = inputColumn.CustomPropertyCollection;
                customProperty = inputColumnCustomProperties["PackedScale"];
                this.outColumnScale[this.numOutColumnWrites++] = (int)customProperty.Value;
            }
        }

        public override void ProcessInput(int inputID, PipelineBuffer buffer)
        {
            System.Decimal result;

            byte[] source;
            if (!buffer.EndOfRowset)
            {
                while (buffer.NextRow())
                {
                    // this component nulls output columns on badly formatted input data
                    // a better implementation would provide an error output and give
                    // user control over disposition on error.
                    for (int j = 0; j < this.numOutColumnWrites; j++)
                    {
                        try
                        {
                            // GetBytes will throw if source column is null
                            source = buffer.GetBytes(this.outColumnSourceIDs[j]);
                            result = Unpack(source, this.outColumnScale[j]);
                            buffer.SetDecimal(this.outColumnWriteIDs[j], result);
                        }
                        catch (Exception)
                        {
                            buffer.SetNull(this.outColumnWriteIDs[j]);
                        }
                    }
                }
            }
        }

        private Decimal Unpack(byte[] inp, int scale)
        {
            long lo = 0;
            long mid = 0;
            long hi = 0;
            bool isNegative;

            // this nybble stores only the sign, not a digit.  
            // "C" hex is positive, "D" hex is negative, and "F" hex is unsigned. 
            switch (nibble(inp, 0))
            {
                case 0x0D:
                    isNegative = true;
                    break;
                case 0x0F:
                case 0x0C:
                    isNegative = false;
                    break;
                default:
                    throw new Exception("Bad sign nibble");
            }
            long intermediate;
            long carry;
            long digit;
            for (int j = inp.Length * 2 - 1; j > 0; j--)
            {
                // multiply by 10
                intermediate = lo * 10;
                lo = intermediate & 0xffffffff; 
                carry = intermediate >> 32;
                intermediate = mid * 10 + carry;
                mid = intermediate & 0xffffffff;
                carry = intermediate >> 32;
                intermediate = hi * 10 + carry;
                hi = intermediate & 0xffffffff;
                carry = intermediate >> 32;
                // By limiting input length to 14, we ensure overflow will never occur

                digit = nibble(inp, j);
                if (digit > 9)
                {
                    throw new Exception("Bad digit");
                }
                intermediate = lo + digit;
                lo = intermediate & 0xffffffff;
                carry = intermediate >> 32;
                if (carry > 0)
                {
                    intermediate = mid + carry;
                    mid = intermediate & 0xffffffff;
                    carry = intermediate >> 32;
                    if (carry > 0)
                    {
                        intermediate = hi + carry;
                        hi = intermediate & 0xffffffff;
                        carry = intermediate >> 32;
                        // carry should never be non-zero. Back up with validation
                    }
                }
            }
            return new Decimal((int) lo, (int) mid, (int) hi, isNegative, (byte) scale);
        }

        private int nibble(byte[] inp, int nibbleNo)
        {
            int b = inp[inp.Length - 1 - nibbleNo / 2];
            return (nibbleNo % 2 == 0) ? (b & 0x0000000F) : (b >> 4);
        }

        #endregion

    }
}