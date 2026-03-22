import {
  Card,
  CardBody,
  CardHeader,
  Col,
  Nav,
  NavItem,
  NavLink,
  TabContent,
  TabPane,
  UncontrolledCollapse,
} from "reactstrap";
import React, { useState } from "react";
import { IconCode, IconDisc, IconHistory, IconStar } from "@tabler/icons-react";

const VerticalTabsRightSide: React.FC = () => {
  const [activeTab, setActiveTab] = useState<string>("vl-features");

  const handleToggle = (tabId: string) => {
    if (activeTab !== tabId) setActiveTab(tabId);
  };

  const tabs = [
    {
      id: "vl-features",
      icon: <IconDisc size={24} className="pe-1 ps-1" />,
      title: "Features",
      content: (
        <p>
          The idea is to use <code>:target</code> pseudoclass to show tabs, use
          anchors with fragment identifiers to switch between them. The idea is
          to use pseudoclass to show tabs, use anchors with fragment identifiers
          to switch between them.
        </p>
      ),
    },
    {
      id: "vl-history",
      icon: <IconHistory size={24} className="pe-1 ps-1" />,
      title: "History",
      content: (
        <ol>
          <li>Show only the last tab.</li>
          <li>
            If <code>:target</code> matches a tab, show it and hide all
            following siblings.
          </li>
          <li>Matches a tab, show it and hide all following siblings.</li>
        </ol>
      ),
    },
    {
      id: "vl-reviews",
      icon: <IconStar size={24} className="pe-1 ps-1" />,
      title: "Reviews",
      content: (
        <p>
          The idea is to use <code>:target</code> pseudoclass to show tabs, use
          anchors with fragment identifiers to switch between them. The idea is
          to use pseudoclass to show tabs, use anchors with fragment identifiers
          to switch between them.
        </p>
      ),
    },
  ];

  return (
    <Col lg={6}>
      <Card>
        <CardHeader className="code-header d-flex justify-content-between align-items-center">
          <h5>Vertical Tabs Right Side</h5>
          <a href="#" id="togglerVerticalRightTabBtn">
            <IconCode data-source="t6" className="source" size={32} />
          </a>
        </CardHeader>
        <CardBody className="vertical-right-tab">
          <Nav
            tabs
            className="app-tabs-secondary me-0 ms-3 flex-column"
            id="vl-bg"
            role="tablist"
          >
            {tabs.map((tab) => (
              <NavItem role="presentation" key={tab.id}>
                <NavLink
                  className={activeTab === tab.id ? "active" : ""}
                  id={`${tab.id}-tab`}
                  type="button"
                  role="tab"
                  aria-controls={`${tab.id}-tab-pane`}
                  aria-selected={activeTab === tab.id}
                  onClick={() => handleToggle(tab.id)}
                >
                  {tab.icon}
                  {tab.title}
                </NavLink>
              </NavItem>
            ))}
          </Nav>
          <TabContent
            activeTab={activeTab}
            className="tab-content text-sm-end mt-3"
            id="vl-bgContent"
          >
            {tabs.map((tab) => (
              <TabPane
                tabId={tab.id}
                id={`${tab.id}-tab-pane`}
                role="tabpanel"
                aria-labelledby={`${tab.id}-tab`}
                className={activeTab === tab.id ? "show active" : ""}
                tabIndex={0}
                key={tab.id}
              >
                {tab.content}
              </TabPane>
            ))}
          </TabContent>
        </CardBody>

        <UncontrolledCollapse toggler="#togglerVerticalRightTabBtn">
          <pre>
            <code className="language-html">{`<Card>
  <CardHeader className="d-flex justify-content-between align-items-center">
    <h5>Vertical Tabs Right Side</h5>
    <a href="#tab6" aria-expanded="false" aria-controls="tab6">
      <IconCode data-source="t6" className="source" size={32} />
    </a>
  </CardHeader>
  <CardBody className="vertical-right-tab">
    <Nav tabs className="app-tabs-secondary me-0 ms-3 flex-column" id="vl-bg" role="tablist">
${tabs
  .map(
    (tab) => `      <NavItem role="presentation">
        <NavLink
          ${activeTab === tab.id ? 'className="active"' : ""}
          id="${tab.id}-tab"
          role="tab"
          aria-controls="${tab.id}-tab-pane"
          aria-selected="${activeTab === tab.id}"
        >
          <!-- Icon omitted -->
          ${tab.title}
        </NavLink>
      </NavItem>`
  )
  .join("\n")}
    </Nav>
    <TabContent activeTab="${activeTab}" className="mt-3" id="vl-bgContent">
${tabs
  .map(
    (tab) => `      <TabPane
        tabId="${tab.id}"
        role="tabpanel"
        id="${tab.id}-tab-pane"
        className="${tab.id === "vl-features" ? "show active" : ""}"
      >
        <!-- Content omitted for brevity -->
      </TabPane>`
  )
  .join("\n")}
    </TabContent>
  </CardBody>
</Card>`}</code>
          </pre>
        </UncontrolledCollapse>
      </Card>
    </Col>
  );
};

export default VerticalTabsRightSide;
