import React, { useState } from "react";
import {
  Row,
  Col,
  Card,
  CardBody,
  Button,
  Nav,
  NavItem,
  NavLink,
  TabPane,
} from "reactstrap";
import classnames from "classnames";
import {
  demoAdvanceui,
  demoAuth,
  demoError,
  demoForms,
  demoIcon,
  demoItems,
  demoReadytouse,
  demos,
  demoTable,
  demoUi,
} from "@/Data/Landing/democard";
import { IconChevronsRight } from "@tabler/icons-react";
import SectionHeading from "@/app/other-pages/(landing)/_components/SectionHeading";
import PreviewBrandImage from "@/Component/CommonElements/PreviewBrandImage";

type DemoItem = {
  imgSrc: string;
  title: string;
  link: string;
  btnClass?: string;
  btnColor?: string;
};

const DemoSection: React.FC = () => {
  const [activeTab, setActiveTab] = useState<string>("dashboard");

  const toggle = (tab: string) => {
    if (activeTab !== tab) setActiveTab(tab);
  };

  return (
    <div>
      <div className="container-fluid">
        <SectionHeading
          title="pages"
          highlight="150+"
          description="All pages created with unlimited features that will reduce the
                cost, efforts and your time."
        />
        <Row>
          <Col xl={8} className="offset-xl-2">
            <div className="demos-tab-section">
              <Nav tabs className="app-tabs-dark" id="v-bg" role="tablist">
                {[
                  { id: "dashboard", icon: "ti-home", label: "Dashboard" },
                  { id: "apps", icon: "ti-server", label: "Apps" },
                  { id: "ui", icon: "ti-first-aid-kit", label: "UI Kits" },
                  {
                    id: "advance-ui",
                    icon: "ti-briefcase",
                    label: "Advance UI",
                  },
                  { id: "icons", icon: "ti-icons", label: "Icons" },
                  { id: "table", icon: "ti-table", label: "Tables" },
                  { id: "forms", icon: "ti-forms", label: "Forms" },
                  {
                    id: "ready-to-use",
                    icon: "ti-table-import",
                    label: "Ready To Use",
                  },
                  { id: "auth", icon: "ti-news", label: "Auth Pages" },
                  { id: "error", icon: "ti-news", label: "Error Pages" },
                ].map((tab) => (
                  <NavItem
                    key={tab.id}
                    role="presentation"
                    className="cursor-pointer"
                  >
                    <NavLink
                      className={classnames({
                        active: activeTab === tab.id,
                      })}
                      onClick={() => toggle(tab.id)}
                      role="tab"
                    >
                      <i className={`ti ${tab.icon} pe-2 ps-1`}></i> {tab.label}
                    </NavLink>
                  </NavItem>
                ))}
              </Nav>
            </div>
          </Col>
          <Col xs={12}>
            <div className="tab-content mt-3">
              {[
                { id: "dashboard", data: demos },
                { id: "apps", data: demoItems },
                { id: "ui", data: demoUi },
                { id: "advance-ui", data: demoAdvanceui },
                { id: "icons", data: demoIcon },
                { id: "table", data: demoTable },
                { id: "forms", data: demoForms },
                { id: "ready-to-use", data: demoReadytouse },
                { id: "auth", data: demoAuth },
                { id: "error", data: demoError },
              ].map((tab) => (
                <TabPane
                  key={tab.id}
                  tabId={tab.id}
                  className={classnames("tab-pane fade", {
                    show: activeTab === tab.id,
                    active: activeTab === tab.id,
                  })}
                  id={`${tab.id}-tab-pane`}
                  role="tabpanel"
                  tabIndex={0}
                >
                  <Row className="justify-content-center">
                    {tab.data.map((item: DemoItem, index: number) => (
                      <Col sm="6" lg="3" key={index}>
                        <Card className="demo-card">
                          <CardBody>
                            <PreviewBrandImage
                              src={item.imgSrc}
                              alt={`${item.title} preview`}
                              className="img-fluid b-r-8"
                            />
                            <div className="demo-box">
                              <h6 className="m-0 f-w-500 f-s-18">
                                {item.title}
                              </h6>
                              <Button
                                href={item.link}
                                target="_blank"
                                role="button"
                                className={`icon-btn b-r-22 ${item.btnClass || ""}`}
                              >
                                <IconChevronsRight size={18} />
                              </Button>
                            </div>
                          </CardBody>
                        </Card>
                      </Col>
                    ))}
                  </Row>
                </TabPane>
              ))}
            </div>
          </Col>
        </Row>
      </div>
    </div>
  );
};

export default DemoSection;
