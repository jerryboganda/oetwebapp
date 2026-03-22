"use client";
import React from "react";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Col,
  Container,
  Row,
  UncontrolledTooltip,
} from "reactstrap";
import Breadcrumbs from "@/Component/CommonElements/Breadcrumbs";
import { IconBriefcase } from "@tabler/icons-react";

interface TooltipItem {
  id: string;
  label: string;
  placement: "top" | "right" | "bottom" | "left";
  color: string;
}
interface TooltipColorItem {
  id: string;
  label: string;
  color: string;
}

const tooltipItems: TooltipItem[] = [
  {
    id: "TooltipTop",
    label: "Tooltip on top",
    placement: "top",
    color: "primary",
  },
  {
    id: "TooltipRight",
    label: "Tooltip on right",
    placement: "right",
    color: "secondary",
  },
  {
    id: "TooltipBottom",
    label: "Tooltip on bottom",
    placement: "bottom",
    color: "success",
  },
  {
    id: "TooltipLeft",
    label: "Tooltip on left",
    placement: "left",
    color: "danger",
  },
];

const colorTooltips: TooltipColorItem[] = [
  { id: "TooltipExample7", label: "Primary", color: "primary" },
  { id: "TooltipExample8", label: "Secondary", color: "secondary" },
  { id: "TooltipExample9", label: "Success", color: "success" },
  { id: "TooltipExample10", label: "Danger", color: "danger" },
  { id: "TooltipExample11", label: "Warning", color: "warning" },
  { id: "TooltipExample12", label: "Info", color: "info" },
  { id: "TooltipExample13", label: "Light", color: "light" },
  { id: "TooltipExample14", label: "Dark", color: "dark" },
];

const TooltipPopoverPage = () => {
  return (
    <>
      <Container fluid className="ui-section">
        <Breadcrumbs
          mainTitle="Tooltip & Popovers"
          title="Advance Ui"
          path={["Tooltip & Popovers"]}
          Icon={IconBriefcase}
        />

        <Row>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Default Tooltips</h5>
              </CardHeader>
              <CardBody>
                <div className="d-flex flex-wrap gap-3">
                  <Button
                    color="primary"
                    type="button"
                    id="TooltipExample1"
                    title="Custom tooltip"
                  >
                    Custom tooltip
                  </Button>
                  <UncontrolledTooltip target="TooltipExample1" placement="top">
                    Custom tooltip
                  </UncontrolledTooltip>

                  <Button type="button" className="bg-secondary-300" disabled>
                    Disabled Tooltips
                  </Button>
                </div>
              </CardBody>
            </Card>
          </Col>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Placement</h5>
              </CardHeader>
              <CardBody className="d-flex flex-wrap gap-3">
                {tooltipItems.map(({ id, label, placement, color }) => (
                  <div key={id}>
                    <Button color={color} type="button" id={id} title={label}>
                      {label}
                    </Button>
                    <UncontrolledTooltip target={id} placement={placement}>
                      Custom tooltip
                    </UncontrolledTooltip>
                  </div>
                ))}
              </CardBody>
            </Card>
          </Col>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>HTML</h5>
              </CardHeader>
              <CardBody>
                <Button
                  type="button"
                  className="btn btn-warning"
                  id="TooltipExample6"
                >
                  Tooltip with HTML
                </Button>
                <UncontrolledTooltip target="TooltipExample6" placement="top">
                  <em>Tooltip</em> <u>with</u> <b>HTML</b>
                </UncontrolledTooltip>
              </CardBody>
            </Card>
          </Col>
          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Colors Tooltips</h5>
              </CardHeader>
              <CardBody className="d-flex flex-wrap gap-2">
                {colorTooltips.map(({ id, label, color }) => (
                  <div key={id}>
                    <Button id={id} type="button" color={color} title={label}>
                      {label}
                    </Button>
                    <UncontrolledTooltip
                      target={id}
                      placement="top"
                      popperClassName={`custom-${color}`}
                    >
                      Custom tooltip
                    </UncontrolledTooltip>
                  </div>
                ))}
              </CardBody>
            </Card>
          </Col>

          <Col xs={12}>
            <Card>
              <CardHeader>
                <h5>Custom Popovers</h5>
              </CardHeader>
              <CardBody>
                <Button
                  id="DismissiblePopover"
                  type="button"
                  color="light-warning"
                >
                  Dismissible popover
                </Button>

                <UncontrolledTooltip
                  trigger="focus"
                  placement="top"
                  target="DismissiblePopover"
                >
                  Dismissible popover
                </UncontrolledTooltip>
              </CardBody>
            </Card>
          </Col>
        </Row>
      </Container>
    </>
  );
};

export default TooltipPopoverPage;
