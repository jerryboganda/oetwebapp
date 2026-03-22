import React from "react";
import { Card, CardHeader, CardBody, Col, Row } from "reactstrap";
import { US, GB, DE, LR } from "country-flag-icons/react/3x2";

// Avatar Tab data
const avatarTabItems = [
  {
    id: "home",
    label: "Guthry",
    position: "Sales Manager",
    avatarSrc: "/images/avatar/5.png",
    content: (
      <p>
        The idea is to use <code>:target</code> pseudoclass to show tabs, use
        anchors with fragment identifiers to switch between them. The idea is to
        use pseudoclass to show tabs, use anchors with fragment identifiers to
        switch between them.
      </p>
    ),
  },
  {
    id: "profile",
    label: "Olive Yew",
    position: "Account Manager",
    avatarSrc: "/images/avatar/1.png",
    content: (
      <ol>
        <li>Show only the last tab.</li>
        <li>
          If <code>:target</code> matches a tab, show it and hide all following
          siblings.
        </li>
        <li>Matches a tab, show it and hide all following siblings.</li>
      </ol>
    ),
  },
  {
    id: "contact",
    label: "Lily",
    position: "Manager",
    avatarSrc: "/images/avatar/14.png",
    content: (
      <p>
        The idea is to use <code>:target</code> pseudoclass to show tabs, use
        anchors with fragment identifiers to switch between them.
      </p>
    ),
  },
];

// Flag Tab data
const flagTabItems = [
  {
    id: "home",
    label: "USA",
    Icon: US,
    content: (
      <p>
        The idea is to use <code>:target</code> pseudoclass to show tabs, use
        anchors with fragment identifiers to switch between them. The idea is to
        use pseudoclass to show tabs, use anchors with fragment identifiers to
        switch between them.
      </p>
    ),
  },
  {
    id: "profile",
    label: "GBR",
    Icon: GB,
    content: (
      <ol>
        <li>Show only the last tab.</li>
        <li>
          If <code>:target</code> matches a tab, show it and hide all following
          siblings.
        </li>
        <li>Matches a tab, show it and hide all following siblings.</li>
      </ol>
    ),
  },
  {
    id: "contact",
    label: "DEU",
    Icon: DE,
    content: (
      <p>
        The idea is to use <code>:target</code> pseudoclass to show tabs, use
        anchors with fragment identifiers to switch between them.
      </p>
    ),
  },
  {
    id: "about",
    label: "LBR",
    Icon: LR,
    content: (
      <ol>
        <li>Show only the last tab.</li>
        <li>
          If <code>:target</code> matches a tab, show it and hide all following
          siblings.
        </li>
        <li>Matches a tab, show it and hide all following siblings.</li>
      </ol>
    ),
  },
];

const TabsWithAvatars: React.FC = () => {
  const [activeAvatarTab, setActiveAvatarTab] = React.useState(
    avatarTabItems[0]?.id || ""
  );
  const [activeFlagTab, setActiveFlagTab] = React.useState(
    flagTabItems[0]?.id || ""
  );
  return (
    <Row>
      {/* Avatar Tabs */}
      <Col lg={6}>
        <Card>
          <CardHeader>
            <h5>Avatar-based Tabs</h5>
          </CardHeader>
          <CardBody className="Implements-tabs">
            <ul
              className="nav nav-tabs tab-light-secondary"
              id="testi-Light"
              role="tablist"
            >
              {avatarTabItems.map(({ id, label, position, avatarSrc }) => (
                <li className="nav-item" role="presentation" key={id}>
                  <button
                    className={`nav-link gap-2 d-flex ${activeAvatarTab === id ? "active" : ""}`}
                    id={`testi-${id}-tab`}
                    onClick={() => setActiveAvatarTab(id)}
                    type="button"
                    role="tab"
                    aria-controls={`testi-${id}-tab-pane`}
                    aria-selected={activeAvatarTab === id}
                  >
                    <span className="h-35 w-35 d-flex-center b-r-50 overflow-hidden text-bg-primary">
                      <img src={avatarSrc} alt="" className="img-fluid" />
                    </span>
                    <span>
                      <span className="text-body d-block text-start f-s-16 f-w-600 text-dark mb-0">
                        {label}
                      </span>
                      <span className="text-start f-s-14 m-0">{position}</span>
                    </span>
                  </button>
                </li>
              ))}
            </ul>

            <div className="tab-content" id="testi-LightContent">
              {avatarTabItems.map(({ id, content }) => (
                <div
                  key={id}
                  className={`tab-pane fade ${activeAvatarTab === id ? "show active" : ""}`}
                  id={`testi-${id}-tab-pane`}
                  role="tabpanel"
                  aria-labelledby={`testi-${id}-tab`}
                  tabIndex={0}
                >
                  {content}
                </div>
              ))}
            </div>
          </CardBody>
        </Card>
      </Col>

      {/* Flag Tabs */}
      <Col lg={6}>
        <Card>
          <CardHeader>
            <h5>Flag-based Tabs</h5>
          </CardHeader>
          <CardBody className="Implements-tabs">
            <ul
              className="nav nav-tabs tab-light-secondary"
              id="lang-Light"
              role="tablist"
            >
              {flagTabItems.map(({ id, label, Icon }, index) => (
                <li className="nav-item" role="presentation" key={id}>
                  <button
                    className={`nav-link gap-2 ${activeFlagTab === id ? "active" : ""}`}
                    id={`lang-${id}-tab`}
                    type="button"
                    onClick={() => setActiveFlagTab(id)}
                    role="tab"
                    aria-controls={`lang-${id}-tab-pane`}
                    aria-selected={index === 0}
                  >
                    <Icon title={label} className="w-22 h-22 overflow-hidden" />
                    {label}
                  </button>
                </li>
              ))}
            </ul>

            <div className="tab-content" id="lang-LightContent">
              {flagTabItems.map(({ id, content }) => (
                <div
                  key={id}
                  className={`tab-pane fade ${activeFlagTab === id ? "show active" : ""}`}
                  id={`lang-${id}-tab-pane`}
                  role="tabpanel"
                  aria-labelledby={`lang-${id}-tab`}
                  tabIndex={0}
                >
                  {content}
                </div>
              ))}
            </div>
          </CardBody>
        </Card>
      </Col>
    </Row>
  );
};

export default TabsWithAvatars;
