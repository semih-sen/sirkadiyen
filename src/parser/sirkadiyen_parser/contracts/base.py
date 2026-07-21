from pydantic import BaseModel, ConfigDict
from pydantic.alias_generators import to_camel


class ContractModel(BaseModel):
    """Base for wire models parsed from an external payload.

    Only camel-case aliases are accepted. The transport contract states that
    JSON properties use camel case, so a snake-case payload is a producer defect
    and must fail validation rather than be silently tolerated.
    """

    model_config = ConfigDict(
        alias_generator=to_camel,
        extra="forbid",
        validate_by_alias=True,
        validate_by_name=False,
        validate_assignment=True,
    )


class OutboundContractModel(ContractModel):
    """Base for wire models the parser constructs in Python.

    These are built from code rather than parsed from an external payload, so
    field names are accepted alongside aliases. Serialization still uses the
    camel-case aliases, leaving the wire format unchanged.
    """

    model_config = ConfigDict(validate_by_name=True)
